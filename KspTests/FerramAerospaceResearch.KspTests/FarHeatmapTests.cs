using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using KspTest;
using UnityEngine;

namespace FerramAerospaceResearch.KspTests
{
    /// <summary>FAR's editor heat map sweep, multithreaded, under the ThreadCanary.</summary>
    public static class FarHeatmapTests
    {
        private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        /// <summary>
        /// What the multithreaded heat map sweep's workers touch that they must not. In the editor with the craft
        /// loaded, runs the sweep (Multithread on, auto-fit off) `sweeps` times with ThreadCanary armed and reports:
        /// FloatCurve instances evaluated from worker threads - which ones overlapped (two threads inside the same
        /// curve at once, the suspected native crash), by how many threads, from where; CelestialBody atmosphere calls
        /// from workers; errors and exceptions logged from any thread; and, while each sweep runs, the objects the
        /// per-slot engine solver clones share (FAR's EditorEngineDeck._clones[engine][slot]) and any Unity object a
        /// clone reaches. Writes hazards.md. Discovery only: passes unless a sweep cannot be started or does not
        /// finish. Progress (cells done) is logged every 10 s; a sweep past its timeout is cancelled so KSP can exit.
        /// Params: craft, craftDir, nMach (24), nAlt (16), machMin, machMax (2.5), altMinKm, altMaxKm (20), sweeps (2),
        /// timeoutSec (600), canary ("off,on": one sweep per entry, the canary armed or not, overriding sweeps),
        /// sample (true: count calls per worker thread in FAR, SolverEngines and RealFuels solver code, so a sweep
        /// that stops progressing for stallSec (30) reports what its workers keep calling, then fails without
        /// cancelling - a cancel joins the stuck workers on the main thread).
        /// </summary>
        [UITest("Far_HeatmapHazards", Kind = "Check", StartsAt = StartState.Vab, Craft = "Mk2 SSTO Big", CraftDir = "VAB", TimeoutSec = 1800)]
        public static IEnumerator Far_HeatmapHazards(TestContext ctx)
        {
            yield return Wait.Seconds(10);   // FAR voxelizes the ship
            Type editorGui = Reflect.FindType("FerramAerospaceResearch.FARGUI.FAREditorGUI.EditorGUI");
            object gui = editorGui?.GetProperty("Instance", Any)?.GetValue(null, null);
            Assert.True(gui != null, "FAR's editor GUI instance");
            object heatmaps = editorGui.GetField("_heatmaps", Any)?.GetValue(gui);
            Assert.True(heatmaps != null, "FAR's heat map GUI (a FAR build with the heat maps)");
            Type hm = heatmaps.GetType();
            void Set(string field, object value)
            {
                FieldInfo f = hm.GetField(field, Any);
                if (f == null) throw new UITestFailure($"HeatmapsGUI has no field {field}");
                f.SetValue(heatmaps, value);
            }
            // Every range field set: the GUI keeps the last session's settings (a speed axis in m/s, say).
            Set("_multithread", true);
            Set("_autoMaxAlt", false);
            Set("_autoMaxSpeed", false);
            Set("_speedInMS", false);
            Set("_nMach", ctx.Param("nMach", "24"));
            Set("_nAlt", ctx.Param("nAlt", "16"));
            Set("_machMin", ctx.Param("machMin", "0.15"));
            Set("_machMax", ctx.Param("machMax", "2.5"));
            Set("_altMinKm", ctx.Param("altMinKm", "0"));
            Set("_altMaxKm", ctx.Param("altMaxKm", "20"));
            MethodInfo start = hm.GetMethod("StartSweep", Any, null, Type.EmptyTypes, null);
            Assert.True(start != null, "HeatmapsGUI.StartSweep()");
            PropertyInfo running = hm.GetProperty("IsSweepRunning", Any);
            object deck = hm.GetField("_deck", Any)?.GetValue(heatmaps);
            FieldInfo clonesField = deck?.GetType().GetField("_clones", Any);
            var engines = deck?.GetType().GetField("_engines", Any)?.GetValue(deck) as IList;
            ctx.Record("engines", engines == null ? "?" : string.Join(", ", engines.Cast<PartModule>().Select(m => $"{m.part.partInfo.name}/{m.GetType().Name}").Distinct()));
            ctx.Record("processors", Environment.ProcessorCount);

            ThreadCanary.EnsurePatched();
            if (ctx.Param("sample", "true") != "false")
            {
                int patched = CallSampler.Patch(new[] { "FerramAerospaceResearch", "SolverEngines", "RealFuels" }, t =>
                {
                    string ns = t.Namespace ?? "", name = t.FullName ?? "";
                    if (t.Assembly.GetName().Name == "RealFuels") return name.Contains("Solver") || name.Contains("ModuleEnginesRF");
                    if (t.Assembly.GetName().Name == "SolverEngines") return true;
                    return ns == "ferram4" || ns == "FerramAerospaceResearch" || ns.StartsWith("FerramAerospaceResearch.FARAeroComponents") ||
                           ns.StartsWith("FerramAerospaceResearch.FARGUI.FAREditorGUI");
                });
                ctx.Record("sampledMethods", patched);
            }
            double timeout = ctx.ParamDouble("timeoutSec", 600), stallSec = ctx.ParamDouble("stallSec", 30);
            string[] plan = (ctx.Param("canary") ?? string.Join(",", Enumerable.Repeat("on", (int)ctx.ParamDouble("sweeps", 2))))
                            .Split(',').Select(x => x.Trim()).ToArray();
            int sweeps = plan.Length;
            FieldInfo cellsDone = hm.GetField("_cellsDone", Any), cellsTotal = hm.GetField("_cellsTotal", Any);
            FieldInfo cancel = hm.GetField("_cancelRequested", Any);
            var shared = new SortedDictionary<string, int>();
            int clonesSeen = 0, slotsSeen = 0;
            ThreadCanary.Arm();
            ThreadCanary.Disarm();   // clears; armed per sweep below
            for (int s = 0; s < sweeps; ++s)
            {
                bool armed = plan[s] != "off";
                ctx.Step($"Sweep {s + 1} of {sweeps}, multithreaded, canary {(armed ? "on" : "off")}");
                if (armed) ThreadCanary.Resume(); else ThreadCanary.Disarm();
                object started = start.Invoke(heatmaps, null);
                string status = hm.GetField("_statusMessage", Any)?.GetValue(heatmaps) as string;
                Assert.True(!(started is bool b) || b, $"sweep {s + 1} started ({status})");
                if (s == 0)
                    ctx.Record("engines", engines == null ? "?" : string.Join(", ", engines.Cast<PartModule>().Select(m => $"{m.part.partInfo.name}/{m.GetType().Name}").Distinct()));
                float t0 = Time.realtimeSinceStartup, lastLog = t0, lastChange = t0;
                object lastDone = null;
                bool walked = false;
                while ((bool)running.GetValue(heatmaps, null))
                {
                    float now = Time.realtimeSinceStartup;
                    object done = cellsDone?.GetValue(heatmaps);
                    if (!Equals(done, lastDone)) { lastDone = done; lastChange = now; }
                    if (now - lastChange > stallSec)
                    {
                        // Stuck: what do the workers keep calling? No cancel - it would join them on the main thread.
                        var a = CallSampler.Snapshot();
                        float ts = Time.realtimeSinceStartup;
                        while (Time.realtimeSinceStartup - ts < 3) yield return null;
                        List<string> busy = CallSampler.Busy(a, CallSampler.Snapshot());
                        ctx.Record($"sweep{s + 1}Stalled", $"{done} of {cellsTotal?.GetValue(heatmaps)} cells, no progress for {stallSec:F0} s; {busy.Count} worker threads busy");
                        for (int k = 0; k < busy.Count; ++k)
                            ctx.Record($"sweep{s + 1}Busy{k + 1}", busy[k]);
                        ThreadCanary.Disarm();
                        WriteReport(ctx, engines, plan, clonesSeen, slotsSeen, shared, busy);
                        throw new UITestFailure($"sweep {s + 1} kept progressing ({done} of {cellsTotal?.GetValue(heatmaps)} cells, then nothing for {stallSec:F0} s; {busy.Count} worker threads still busy)");
                    }
                    if (now - lastLog >= 10)
                    {
                        lastLog = now;
                        Log.Info($"heatmap sweep {s + 1}: {cellsDone?.GetValue(heatmaps)} of {cellsTotal?.GetValue(heatmaps)} cells after {now - t0:F0} s");
                    }
                    if (now - t0 > timeout)
                    {
                        ctx.Record($"sweep{s + 1}Stuck", $"{cellsDone?.GetValue(heatmaps)} of {cellsTotal?.GetValue(heatmaps)} cells after {now - t0:F0} s");
                        cancel?.SetValue(heatmaps, true);
                        float tc = Time.realtimeSinceStartup;
                        while ((bool)running.GetValue(heatmaps, null) && Time.realtimeSinceStartup - tc < 60)
                            yield return null;
                        ctx.Record($"sweep{s + 1}CancelJoined", !(bool)running.GetValue(heatmaps, null));
                        ThreadCanary.Disarm();
                        WriteReport(ctx, engines, plan, clonesSeen, slotsSeen, shared, null);
                        throw new UITestFailure($"sweep {s + 1} finished within {timeout} s ({cellsDone?.GetValue(heatmaps)} of {cellsTotal?.GetValue(heatmaps)} cells)");
                    }
                    if (!walked && clonesField?.GetValue(deck) is object[][] clones)
                    {
                        walked = true;
                        for (int ei = 0; ei < clones.Length; ++ei)
                        {
                            object[] perSlot = clones[ei];
                            if (perSlot == null) continue;
                            ++clonesSeen;
                            slotsSeen = Math.Max(slotsSeen, perSlot.Length);
                            string engine = engines != null && ei < engines.Count ? ((PartModule)engines[ei]).part.partInfo.name : $"engine {ei}";
                            foreach (string item in ThreadCanary.SharedBetween(perSlot))
                            {
                                string key = $"{engine}: {item}";
                                shared.TryGetValue(key, out int n);
                                shared[key] = n + 1;
                            }
                        }
                    }
                    yield return null;
                }
                ctx.Record($"sweep{s + 1}Sec", $"{Math.Round(Time.realtimeSinceStartup - t0, 1)} (canary {(armed ? "on" : "off")})");
                ctx.Record($"sweep{s + 1}ClonesWalked", walked);
            }
            ThreadCanary.Disarm();
            WriteReport(ctx, engines, plan, clonesSeen, slotsSeen, shared, null);
        }

        private static void WriteReport(TestContext ctx, IList engines, string[] plan, int clonesSeen, int slotsSeen,
                                        SortedDictionary<string, int> shared, List<string> busy)
        {
            int sweeps = plan.Length;
            var curves = ThreadCanary.WorkerCurves();
            var bodies = ThreadCanary.WorkerBodyCalls();
            var logged = ThreadCanary.LoggedErrors();
            int overlapped = curves.Count(c => c.overlapped);
            ctx.Record("workerCurves", curves.Count);
            ctx.Record("overlappedCurves", overlapped);
            ctx.Record("workerBodyCallSites", bodies.Count);
            ctx.Record("sharedCloneObjects", shared.Count);
            ctx.Record("loggedErrors", logged.Count);

            var md = new StringBuilder("# Heat map sweep: what the worker threads touch\n\n");
            md.Append($"Craft {ctx.Param("craft", "Mk2 SSTO Big")}; engines: {(engines == null ? "?" : string.Join(", ", engines.Cast<PartModule>().Select(m => $"{m.part.partInfo.name} ({m.GetType().Name})").Distinct()))}; ");
            md.Append($"{sweeps} multithreaded sweeps (canary {string.Join(", ", plan)}), grid {ctx.Param("nMach", "24")} x {ctx.Param("nAlt", "16")}; {Environment.ProcessorCount} processors; ");
            md.Append($"engine solver clones walked for {clonesSeen} engines x {slotsSeen} slots.\n\n");
            md.Append($"## FloatCurves evaluated on worker threads: {curves.Count}, of which {overlapped} overlapped\n\n");
            md.Append("Overlapped = two threads inside the same curve's Evaluate at once (a Unity AnimationCurve underneath, not safe to share).\n\n");
            md.Append("| overlapped | threads | calls | first worker caller |\n|---|---|---|---|\n");
            foreach (var c in curves.GroupBy(c => (c.overlapped, c.caller)).OrderByDescending(g => g.Key.overlapped).ThenByDescending(g => g.Max(x => x.threads)))
                md.Append($"| {(c.Key.overlapped ? "**yes**" : "no")} | {c.Max(x => x.threads)} | {c.Sum(x => x.calls)} ({c.Count()} curves) | {c.Key.caller} |\n");
            md.Append($"\n## CelestialBody atmosphere calls from worker threads: {bodies.Count} call sites\n\n");
            foreach (var kv in bodies.OrderByDescending(kv => kv.Value))
                md.Append($"- {kv.Key}: {kv.Value}\n");
            md.Append($"\n## Shared between the per-slot engine solver clones: {shared.Count}\n\n");
            foreach (var kv in shared)
                md.Append($"- {kv.Key}\n");
            if (busy != null)
            {
                md.Append($"\n## Stuck: what each busy worker thread called in 3 s after progress stopped\n\n");
                foreach (string line in busy)
                    md.Append($"- {line}\n");
            }
            md.Append($"\n## Errors and exceptions logged during the sweeps: {logged.Count}\n\n```\n");
            md.Append(string.Join("\n", logged.Take(200))).Append("\n```\n");
            File.WriteAllText(Path.Combine(ctx.ResultsDir, "hazards.md"), md.ToString());
        }

        /// <summary>
        /// Whether the multithreaded heat map sweep gives exactly what the serial one does. In the editor with the
        /// craft loaded: one sweep with Multithread off, then `parallel` sweeps with it on, every range field set the
        /// same, stability included. Every number of every map (each engine type's envelope, its mass samples, the
        /// stability cells and the per-map maxima) is taken as its bits after each sweep and compared with the serial
        /// sweep's. Passes when every value is bit-identical. Writes serial-vs-parallel.md (per map and field: values
        /// compared, values differing, largest relative difference). Also logs errors from any thread.
        /// Params: craft, craftDir, nMach (16), nAlt (12), machMin (0.15), machMax (2.5), altMinKm (0), altMaxKm (20),
        /// parallel (2), order (sweeps "+"-separated, each serial or parallel, the first the reference; default serial and
        /// `parallel` parallel ones), stability (true), timeoutSec (600).
        /// </summary>
        [UITest("Far_HeatmapSerialVsParallel", Kind = "Check", StartsAt = StartState.Sph, Craft = "Aeris 4A", CraftDir = "SPH", TimeoutSec = 2400)]
        public static IEnumerator Far_HeatmapSerialVsParallel(TestContext ctx)
        {
            yield return Wait.Seconds(10);   // FAR voxelizes the ship
            Type editorGui = Reflect.FindType("FerramAerospaceResearch.FARGUI.FAREditorGUI.EditorGUI");
            object gui = editorGui?.GetProperty("Instance", Any)?.GetValue(null, null);
            Assert.True(gui != null, "FAR's editor GUI instance");
            object heatmaps = editorGui.GetField("_heatmaps", Any)?.GetValue(gui);
            Assert.True(heatmaps != null, "FAR's heat map GUI (a FAR build with the heat maps)");
            Type hm = heatmaps.GetType();
            void Set(string field, object value)
            {
                FieldInfo f = hm.GetField(field, Any);
                if (f == null) throw new UITestFailure($"HeatmapsGUI has no field {field}");
                f.SetValue(heatmaps, value);
            }
            Set("_autoMaxAlt", false);
            Set("_autoMaxSpeed", false);
            Set("_speedInMS", false);
            Set("_nMach", ctx.Param("nMach", "16"));
            Set("_nAlt", ctx.Param("nAlt", "12"));
            Set("_machMin", ctx.Param("machMin", "0.15"));
            Set("_machMax", ctx.Param("machMax", "2.5"));
            Set("_altMinKm", ctx.Param("altMinKm", "0"));
            Set("_altMaxKm", ctx.Param("altMaxKm", "20"));
            Set("_includeStability", ctx.Param("stability", "true") != "false");
            MethodInfo start = hm.GetMethod("StartSweep", Any, null, Type.EmptyTypes, null);
            Assert.True(start != null, "HeatmapsGUI.StartSweep()");
            PropertyInfo running = hm.GetProperty("IsSweepRunning", Any);
            FieldInfo resultsField = hm.GetField("_results", Any);
            double timeout = ctx.ParamDouble("timeoutSec", 600);
            int parallel = (int)ctx.ParamDouble("parallel", 2);
            // the sweeps in order, each "serial" or "parallel"; the first is the reference
            string[] order = (ctx.Param("order") ?? "serial" + string.Concat(Enumerable.Repeat("+parallel", parallel))).Split('+');
            ThreadCanary.EnsurePatched();
            ThreadCanary.Arm();
            ThreadCanary.Disarm();   // clears; only the error log is read below

            // The sweep samples the atmosphere at the game's UT; pin it (param ut) so the air does not depend on the
            // save. (Even pinned, ambient temperature still wobbles ~2e-8 between sessions: float-level session state.)
            double utBefore = Planetarium.GetUniversalTime();
            ctx.Record("utBefore", utBefore.ToString("R"));
            double utPinned = ctx.ParamDouble("ut", 0);
            HighLogic.CurrentGame.flightState.universalTime = utPinned;
            if (Planetarium.fetch != null)
                Planetarium.SetUniversalTime(utPinned);
            ctx.Record("ut", Planetarium.GetUniversalTime().ToString("R"));

            // FAR's voxelization is not repeatable (body drag moves ~1 % between voxelizations of one craft), and
            // the editor can revoxelize between sweeps, so each sweep records a fingerprint of the voxel sections it
            // ran on, and an order entry "voxel" forces a revoxelization. Compare only sweeps with equal fingerprints.
            object vehicleAero = editorGui.GetField("_vehicleAero", Any)?.GetValue(gui);
            Assert.True(vehicleAero != null, "EditorGUI._vehicleAero");
            PropertyInfo queued = editorGui.GetProperty("VoxelizationUpdateQueued", Any);
            PropertyInfo voxelizing = vehicleAero.GetType().GetProperty("Voxelizing", Any);
            FieldInfo crossSections = vehicleAero.GetType().GetField("_vehicleCrossSection", Any);
            bool Busy() => (bool)queued.GetValue(gui, null) || (bool)voxelizing.GetValue(vehicleAero, null);
            string Fingerprint()
            {
                ulong h = 14695981039346656037;
                var a = (Array)crossSections.GetValue(vehicleAero);
                FieldInfo area = a.GetType().GetElementType().GetField("area");
                foreach (object s in a)
                {
                    h ^= (ulong)BitConverter.DoubleToInt64Bits((double)area.GetValue(s));
                    h *= 1099511628211;
                }
                double len = (double)vehicleAero.GetType().GetProperty("Length").GetValue(vehicleAero, null);
                return $"{a.Length}:{len:R}:{h:x16}";
            }
            IEnumerator WaitIdle()
            {
                float t = Time.realtimeSinceStartup;
                for (int still = 0; still < 50; still = Busy() ? 0 : still + 1)
                {
                    if (Time.realtimeSinceStartup - t > 120) throw new UITestFailure("voxelization idle within 120 s");
                    yield return null;
                }
            }
            var voxelLog = new StringBuilder();

            var runs = new List<(string name, SortedDictionary<string, string> values)>();
            for (int r = 0; r < order.Length; ++r)
            {
                if (order[r] == "voxel")
                {
                    ctx.Step("Revoxelize");
                    editorGui.GetMethod("RequestUpdateVoxel", Any).Invoke(null, null);
                    for (float t = Time.realtimeSinceStartup; (bool)queued.GetValue(gui, null) && Time.realtimeSinceStartup - t < 60;)
                        yield return null;
                    continue;
                }
                bool multi = order[r] == "parallel";
                string name = $"{order[r]} {r + 1}";
                yield return WaitIdle();
                string fp = Fingerprint();
                ctx.Record(name.Replace(" ", "") + "Voxel", fp);
                voxelLog.Append(name.Replace(" ", "")).Append('\t').Append(fp).Append('\n');
                File.WriteAllText(Path.Combine(ctx.ResultsDir, "voxels.txt"), voxelLog.ToString());
                ctx.Step($"Sweep {name}");
                Set("_multithread", multi);
                object started = start.Invoke(heatmaps, null);
                string status = hm.GetField("_statusMessage", Any)?.GetValue(heatmaps) as string;
                Assert.True(!(started is bool b) || b, $"{name} sweep started ({status})");
                float t0 = Time.realtimeSinceStartup;
                while ((bool)running.GetValue(heatmaps, null))
                {
                    if (Time.realtimeSinceStartup - t0 > timeout)
                    {
                        hm.GetField("_cancelRequested", Any)?.SetValue(heatmaps, true);
                        throw new UITestFailure($"{name} sweep finished within {timeout} s");
                    }
                    yield return null;
                }
                ctx.Record(name.Replace(" ", "") + "Sec", Math.Round(Time.realtimeSinceStartup - t0, 1));
                var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
                if (resultsField.GetValue(heatmaps) is IList results)
                    for (int m = 0; m < results.Count; ++m)
                        Flatten($"map{m}", results[m], values, 0);
                Assert.True(values.Count > 0, $"{name} sweep left results");
                runs.Add((name, values));
                // every value, for comparing sweeps across builds
                File.WriteAllLines(Path.Combine(ctx.ResultsDir, name.Replace(" ", "") + ".values"), values.Select(kv => kv.Key + "\t" + kv.Value));
            }

            // per map and top-level field: compared, differing, largest relative difference
            var md = new StringBuilder("# Heat map sweep: serial against multithreaded\n\n");
            md.Append($"Craft {ctx.Param("craft", "Aeris 4A")}; grid {ctx.Param("nMach", "16")} x {ctx.Param("nAlt", "12")}; {Environment.ProcessorCount} processors; ");
            md.Append($"{runs[0].values.Count} values per sweep.\n\n");
            int totalDiff = 0;
            for (int r = 1; r < runs.Count; ++r)
            {
                var groups = new SortedDictionary<string, (int n, int diff, double worst)>(StringComparer.Ordinal);
                var csv = new StringBuilder("key,serial,other\n");
                foreach (var kv in runs[0].values)
                {
                    // by field, the cell and sample indices left out
                    string group = System.Text.RegularExpressions.Regex.Replace(kv.Key, @"\[[0-9,]+\]", "");
                    groups.TryGetValue(group, out var g);
                    g.n++;
                    if (!runs[r].values.TryGetValue(kv.Key, out string other) || other != kv.Value)
                    {
                        g.diff++;
                        g.worst = Math.Max(g.worst, Rel(kv.Value, other));
                        csv.Append(kv.Key).Append(',').Append(Num(kv.Value).ToString("R")).Append(',').Append(Num(other).ToString("R")).Append('\n');
                    }
                    groups[group] = g;
                }
                File.WriteAllText(Path.Combine(ctx.ResultsDir, $"{runs[0].name.Replace(" ", "")}-vs-{runs[r].name.Replace(" ", "")}.csv"), csv.ToString());
                int missing = runs[r].values.Keys.Count(k => !runs[0].values.ContainsKey(k));
                int diffs = groups.Values.Sum(g => g.diff) + missing;
                totalDiff += diffs;
                ctx.Record($"{runs[r].name.Replace(" ", "")}Differing", $"{diffs} of {runs[0].values.Count}");
                md.Append($"## {runs[0].name} vs {runs[r].name}: {diffs} values differ{(missing > 0 ? $" ({missing} only in {runs[r].name})" : "")}\n\n");
                md.Append("| map.field (all cells) | values | differ | largest relative difference |\n|---|---|---|---|\n");
                foreach (var kv in groups)
                    md.Append($"| {kv.Key} | {kv.Value.n} | {kv.Value.diff} | {(kv.Value.diff > 0 ? kv.Value.worst.ToString("G3") : "-")} |\n");
                md.Append('\n');
            }
            var logged = ThreadCanary.LoggedErrors();
            ctx.Record("loggedErrors", logged.Count);
            md.Append($"## Errors and exceptions logged: {logged.Count}\n\n```\n").Append(string.Join("\n", logged.Take(100))).Append("\n```\n");
            File.WriteAllText(Path.Combine(ctx.ResultsDir, "serial-vs-parallel.md"), md.ToString());
            Assert.True(totalDiff == 0, $"every multithreaded sweep bit-identical to the serial one ({totalDiff} values differ)");
        }

        // Every number reachable from a result (fields, struct fields, arrays of them) as "path=bits", depth-limited.
        private static void Flatten(string path, object o, IDictionary<string, string> into, int depth)
        {
            switch (o)
            {
                case null: return;
                case double d: into[path] = "d" + BitConverter.DoubleToInt64Bits(d).ToString("x16"); return;
                case float f: into[path] = "f" + BitConverter.ToInt32(BitConverter.GetBytes(f), 0).ToString("x8"); return;
                case bool b: into[path] = b ? "T" : "F"; return;
                case string _: return;
            }
            Type t = o.GetType();
            if (t.IsPrimitive || t.IsEnum) { into[path] = Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture); return; }
            if (depth > 6) return;
            if (o is Array a)
            {
                int[] idx = new int[a.Rank];
                for (int i = 0; i < a.Length; ++i)
                {
                    int rem = i;
                    for (int k = a.Rank - 1; k >= 0; --k) { int len = a.GetLength(k); idx[k] = rem % len; rem /= len; }
                    Flatten(path + "[" + string.Join(",", idx) + "]", a.GetValue(idx), into, depth + 1);
                }
                return;
            }
            if (o is UnityEngine.Object || o is Delegate || o is IEnumerable) return;
            foreach (FieldInfo f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                Flatten(path + "." + f.Name, f.GetValue(o), into, depth + 1);
        }

        private static double Rel(string a, string b)
        {
            double A = Num(a), B = Num(b);
            if (double.IsNaN(A) || double.IsNaN(B)) return double.PositiveInfinity;
            return Math.Abs(A - B) / Math.Max(Math.Max(Math.Abs(A), Math.Abs(B)), 1e-300);
        }

        private static double Num(string s)
        {
            if (s == null || s.Length < 2) return double.NaN;
            if (s[0] == 'd') return BitConverter.Int64BitsToDouble(long.Parse(s.Substring(1), System.Globalization.NumberStyles.HexNumber));
            if (s[0] == 'f') return BitConverter.ToSingle(BitConverter.GetBytes(int.Parse(s.Substring(1), System.Globalization.NumberStyles.HexNumber)), 0);
            return double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
        }
    }
}
