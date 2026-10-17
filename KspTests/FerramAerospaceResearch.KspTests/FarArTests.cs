using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using KspTest;
using UnityEngine;

namespace FerramAerospaceResearch.KspTests
{
    /// <summary>
    /// The wing aspect ratio change (FAR's fix/spanwise-aspect-ratio) in flight: the same crafts flown on one FAR build
    /// per run, the runs compared afterwards (upstream against the patch).
    /// </summary>
    public static class FarArTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>
        /// Each craft air-started level at the point and flown on AtmosphereAutopilot's cruise (course 90, the altitude,
        /// the speed from the Mach), then climbed on the same autopilot to climbTo and cruised again there. Each cruise,
        /// once steady (altitude within 60 m, speed within 4 m/s, vertical speed under 3 m/s for steadySec, or
        /// settleTimeoutSec and flagged), is measured over windowSec: AoA, pitch input, FAR's lift and drag (kN, across
        /// and along the airflow; wing models and voxel parts apart), CL and CD on the wing area (the FAR wing parts' S
        /// summed, control surfaces in), L/D, lift over weight and g; then FAR's prediction at the same state with the
        /// airflow turned 1 deg either way gives the lift slope (per radian on the wing area), and the parts' own numbers
        /// go to ar-parts.csv. The climb is measured from the autopilot's new target until within 60 m of it (or
        /// climbTimeoutSec, flagged): its time, mean climb rate, AoA and pitch input (mean and peak), lowest speed and
        /// peak g. After each cruise, FAR's aero visualization (Cl tint, and any other in tints; with arrows, its lift and
        /// drag arrows too) is shot with the UI hidden from above, the side and behind, in daylight. Reaction wheels off.
        /// With ffmpeg given, each craft's flight is recorded (the KSP window, from the air start to the last shot) and
        /// kept only when something went wrong: parts lost, a cruise never steady, its AoA unsteady (SD over 0.5 deg) or
        /// its pitch input near its stop (over 0.9), or the climb not reaching its altitude.
        /// Params: crafts ("+"-separated stock SPH crafts, default every stock FAR plane but the Stearwing), point
        /// (altM:mach, 3000:0.5), climbTo (altM:mach, 7000:0.5; none for one cruise only), steadySec (8), windowSec (10),
        /// settleTimeoutSec (120), climbTimeoutSec (300), tints (cl; also none, cd, stall), clSat (0.25; FAR's default 0.5
        /// leaves cruise lift faint), arrows (on), ffmpeg (path to ffmpeg.exe; without it, no video).
        /// Writes ar-cruise.csv (every frame, with its phase), ar-parts.csv, ar-summary.csv (one row a craft and phase:
        /// cruise, climb, cruise2), ar_*.png and ar_*.mp4.
        /// </summary>
        [UITest("Far_ArCruise", StartsAt = StartState.SpaceCenter, TimeoutSec = 10800)]
        public static IEnumerator Far_ArCruise(TestContext ctx)
        {
            ctx.Record("farBuild", FarSim.FarVersion());
            Assert.True(Aa.Present, "AtmosphereAutopilot is installed");
            string[] crafts = (ctx.Param("crafts") ?? "Aeris 3A+Aeris 4A+Albatross 3+Dove+FAR Firehound MS+FAR SkyEye+Gull+Mallard+" +
                                                      "Ravenspear Mk3+Ravenspear Mk4+Skywinder AE1+Thunderbird+Velociteze").Split('+');
            var points = new List<(string phase, double alt, double mach)>();
            string[] pt = (ctx.Param("point") ?? "3000:0.5").Split(':');
            points.Add(("cruise", double.Parse(pt[0], Inv), double.Parse(pt[1], Inv)));
            string climbTo = ctx.Param("climbTo") ?? "7000:0.5";
            if (!climbTo.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                string[] p2 = climbTo.Split(':');
                points.Add(("cruise2", double.Parse(p2[0], Inv), double.Parse(p2[1], Inv)));
            }
            double steadySec = ctx.ParamDouble("steadySec", 8), windowSec = ctx.ParamDouble("windowSec", 10);
            double timeout = ctx.ParamDouble("settleTimeoutSec", 120), climbTimeout = ctx.ParamDouble("climbTimeoutSec", 300);
            string[] tints = (ctx.Param("tints") ?? "cl").Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s.Length > 0).ToArray();
            double clSat = ctx.ParamDouble("clSat", 0.25);
            bool arrows = !(ctx.Param("arrows", "on") ?? "on").Equals("off", StringComparison.OrdinalIgnoreCase);
            string ffmpeg = ctx.Param("ffmpeg");
            ctx.Record("video", ffmpeg != null && File.Exists(ffmpeg));
            var csv = new StringBuilder("craft,phase,ut,altM,srfSpeed,mach,qKPa,aoaDeg,pitchInput,verticalSpeed,liftKN,dragKN,wingLiftKN,bodyLiftKN,CL,CD,LD,liftOverWeight,g\n");
            var partsCsv = new StringBuilder("craft,phase,part,persistentId,kind,areaM2,effective_AR,Cl,Cd,liftslope,stall,liftKN,dragKN,crossKN,tintCl,shielded\n");
            var summary = new StringBuilder("craft,phase,steady,durationS,altM,srfSpeed,mach,qKPa,massT,wingAreaM2,aoaDeg,aoaSd,aoaMax,pitchInput,pitchInputMax," +
                                            "verticalSpeed,speedMin,liftKN,dragKN,wingLiftKN,bodyLiftKN,CL,CD,LD,liftOverWeight,g,gMax,liftSlopePerRad,predLiftKN,meanAR\n");
            bool[] trajectories = FarFlightProbe.QuietOtherPredictors();
            Recorder rec = null;
            try
            {
                foreach (string craftRaw in crafts)
                {
                    string craft = craftRaw.Trim();
                    string path = ctx.CraftPath(craft, "SPH");
                    if (!File.Exists(path))
                        path = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "Ships", "SPH", craft + ".craft"));
                    string missing = MissingParts(path);
                    if (missing.Length > 0)
                    {
                        ctx.Record(craft + ".skipped", "missing parts: " + missing);
                        continue;
                    }
                    ctx.Step("Air start " + craft);
                    yield return Nav.EnsureState(ctx, StartState.Flight, path, "Runway");
                    yield return Wait.Seconds(5);   // FAR voxelizes the craft after load
                    Vessel v = FlightGlobals.ActiveVessel;
                    if (ffmpeg != null && File.Exists(ffmpeg))
                        rec = Recorder.Start(ffmpeg, Path.Combine(ctx.ResultsDir, $"ar_{craft.Replace(' ', '_')}.mp4"));
                    Flight.IgnoreMaxTemperature(true);
                    foreach (Part p in v.parts)
                        foreach (PartModule m in p.Modules)
                            if (m is ModuleReactionWheel rw)
                            {
                                rw.wheelState = ModuleReactionWheel.WheelState.Disabled;
                                rw.authorityLimiter = 0;
                            }
                    Ctrl.Sas(false);
                    Ctrl.Stage();
                    CelestialBody body = v.mainBody;
                    double SpeedAt(double alt, double m)
                    {
                        double pr = body.GetPressure(alt);
                        return m * body.GetSpeedOfSound(pr, body.GetDensity(pr, body.GetTemperature(alt)));
                    }

                    var start = new FarFlightProbe.Condition { Name = "air start", AltM = points[0].alt, Mach = points[0].mach, AoADeg = 3, Lat = 0, Lon = DaylightLon(body, points[0].alt) };
                    yield return FarFlightProbe.Place(start);
                    Flight.PointAtAoA(start.AoADeg);
                    Ctrl.RetractGear();
                    Ctrl.SetThrottle(1f);
                    int parts = FlightGlobals.ActiveVessel.parts.Count;
                    double area = WingArea(FlightGlobals.ActiveVessel);
                    bool abnormal = false, broken = false;

                    for (int ph = 0; ph < points.Count && !broken; ph++)
                    {
                        var (phase, altM, mach) = points[ph];
                        double speed = SpeedAt(altM, mach);
                        Aa.Cruise(FlightGlobals.ActiveVessel, altM, speed, 90);

                        if (ph > 0)
                        {
                            // the climb: from the new target until within 60 m of it
                            ctx.Step($"{craft}: climb to {altM / 1000:0.#} km");
                            var climb = new Dictionary<string, List<double>>();
                            void AddC(string k, double x) { if (!climb.TryGetValue(k, out var l)) climb[k] = l = new List<double>(); l.Add(x); }
                            double t0 = Planetarium.GetUniversalTime(), climbEnd = t0 + climbTimeout;
                            bool reached = false;
                            var climbWait = new WaitForFixedUpdate();
                            while (Planetarium.GetUniversalTime() < climbEnd)
                            {
                                yield return climbWait;
                                Vessel w = FlightGlobals.ActiveVessel;
                                if (w == null || w.parts.Count < parts) { broken = true; break; }
                                if (Math.Abs(w.altitude - altM) < 60) { reached = true; break; }
                                if (!Forces(w, out Vector3 cw, out Vector3 cb)) continue;
                                LiftDrag(w, cw + cb, w.srf_velocity, out double L, out double D);
                                double q = w.dynamicPressurekPa;
                                AddC("aoa", Flight.AoA); AddC("input", w.ctrlState.pitch); AddC("vs", w.verticalSpeed); AddC("speed", w.srfSpeed);
                                AddC("g", w.geeForce); AddC("alt", w.altitude); AddC("mach", w.mach); AddC("q", q); AddC("L", L); AddC("D", D);
                                AddC("LD", L / D); AddC("mass", w.totalMass);
                                csv.Append(string.Join(",", Csv(craft), "climb", Planetarium.GetUniversalTime().ToString("F3", Inv), F(w.altitude, "F1"),
                                    F(w.srfSpeed, "F2"), F(w.mach, "F4"), F(q, "F4"), F(Flight.AoA, "F4"), F(w.ctrlState.pitch, "F5"), F(w.verticalSpeed, "F2"),
                                    F(L, "G6"), F(D, "G6"), "", "", F(L / (q * area), "F5"), F(D / (q * area), "F5"), F(L / D, "F3"), "", F(w.geeForce, "F3"))).Append('\n');
                            }
                            double C(string k, Func<IEnumerable<double>, double> f) => climb.TryGetValue(k, out var l) && l.Count > 0 ? f(l) : double.NaN;
                            double dur = Planetarium.GetUniversalTime() - t0;
                            ctx.Record($"{craft}.climb.reached", reached);
                            ctx.Record($"{craft}.climb.durationS", Math.Round(dur, 1));
                            ctx.Record($"{craft}.climb.verticalSpeed", Math.Round(C("vs", l => l.Average()), 2));
                            ctx.Record($"{craft}.climb.pitchInputMax", Math.Round(C("input", l => l.Max()), 3));
                            abnormal |= !reached;
                            summary.Append(string.Join(",", Csv(craft), "climb", reached ? "1" : "0", F(dur, "F1"), F(C("alt", l => l.Average()), "F1"),
                                F(C("speed", l => l.Average()), "F2"), F(C("mach", l => l.Average()), "F4"), F(C("q", l => l.Average()), "F3"),
                                F(C("mass", l => l.Average()), "F3"), F(area, "F3"), F(C("aoa", l => l.Average()), "F4"), "", F(C("aoa", l => l.Max()), "F4"),
                                F(C("input", l => l.Average()), "F5"), F(C("input", l => l.Max()), "F5"), F(C("vs", l => l.Average()), "F3"),
                                F(C("speed", l => l.Min()), "F2"), F(C("L", l => l.Average()), "F3"), F(C("D", l => l.Average()), "F3"), "", "", "", "",
                                F(C("LD", l => l.Average()), "F3"), "", F(C("g", l => l.Average()), "F3"), F(C("g", l => l.Max()), "F3"), "", "", "")).Append('\n');
                            if (broken)
                                break;
                        }

                        ctx.Step($"{craft}: {phase} at {altM / 1000:0.#} km, Mach {mach:0.##}: steady then measured");
                        double since = double.NaN, tSettle = Planetarium.GetUniversalTime(), deadline = tSettle + timeout;
                        bool steady = false;
                        while (Planetarium.GetUniversalTime() < deadline)
                        {
                            Vessel w = FlightGlobals.ActiveVessel;
                            if (w == null || w.parts.Count < parts) break;
                            bool now = Math.Abs(w.altitude - altM) < 60 && Math.Abs(w.srfSpeed - speed) < 4 && Math.Abs(w.verticalSpeed) < 3;
                            double ut = Planetarium.GetUniversalTime();
                            if (!now) since = double.NaN;
                            else if (double.IsNaN(since)) since = ut;
                            else if (ut - since >= steadySec) { steady = true; break; }
                            yield return null;
                        }
                        double settle = Planetarium.GetUniversalTime() - tSettle;
                        v = FlightGlobals.ActiveVessel;
                        if (v == null || v.parts.Count < parts)
                        {
                            broken = true;
                            break;
                        }
                        ctx.Record($"{craft}.{phase}.steady", steady);

                        var cols = new Dictionary<string, List<double>>();
                        void Add(string k, double x) { if (!cols.TryGetValue(k, out var l)) cols[k] = l = new List<double>(); l.Add(x); }
                        var fixedWait = new WaitForFixedUpdate();
                        double end = Planetarium.GetUniversalTime() + windowSec;
                        bool partsTaken = false;
                        while (Planetarium.GetUniversalTime() < end)
                        {
                            yield return fixedWait;
                            v = FlightGlobals.ActiveVessel;
                            if (!Forces(v, out Vector3 wing, out Vector3 bodyF)) continue;
                            Vector3 vel = v.srf_velocity;
                            LiftDrag(v, wing + bodyF, vel, out double L, out double D);
                            LiftDrag(v, wing, vel, out double Lw, out _);
                            LiftDrag(v, bodyF, vel, out double Lb, out _);
                            double q = v.dynamicPressurekPa, weight = v.totalMass * FlightGlobals.getGeeForceAtPosition(v.CoM).magnitude;
                            double CL = L / (q * area), CD = D / (q * area);
                            Add("alt", v.altitude); Add("speed", v.srfSpeed); Add("mach", v.mach); Add("q", q); Add("aoa", Flight.AoA);
                            Add("input", v.ctrlState.pitch); Add("vs", v.verticalSpeed); Add("L", L); Add("D", D); Add("Lw", Lw); Add("Lb", Lb);
                            Add("CL", CL); Add("CD", CD); Add("LD", L / D); Add("LW", L / weight); Add("g", v.geeForce); Add("mass", v.totalMass);
                            csv.Append(string.Join(",", Csv(craft), phase, Planetarium.GetUniversalTime().ToString("F3", Inv), F(v.altitude, "F1"), F(v.srfSpeed, "F2"),
                                F(v.mach, "F4"), F(q, "F4"), F(Flight.AoA, "F4"), F(v.ctrlState.pitch, "F5"), F(v.verticalSpeed, "F2"), F(L, "G6"), F(D, "G6"),
                                F(Lw, "G6"), F(Lb, "G6"), F(CL, "F5"), F(CD, "F5"), F(L / D, "F3"), F(L / weight, "F4"), F(v.geeForce, "F3"))).Append('\n');
                            if (!partsTaken && Planetarium.GetUniversalTime() > end - windowSec / 2)
                            {
                                partsTaken = true;
                                PartForces(v, craft, phase, partsCsv);
                            }
                        }
                        double M(string k) => cols.TryGetValue(k, out var l) && l.Count > 0 ? l.Average() : double.NaN;
                        double Mx(string k) => cols.TryGetValue(k, out var l) && l.Count > 0 ? l.Max() : double.NaN;
                        FarFlightProbe.Stats(cols.TryGetValue("aoa", out var al) ? al : new List<double>(), out double aoa, out double aoaSd);

                        // FAR's prediction at this state, the airflow turned 1 deg about the pitch axis either way (after the
                        // window: a prediction leaves state on the wings the next physics frame can read)
                        double slope = double.NaN, predL = double.NaN;
                        {
                            Vector3 vel = v.srf_velocity, axis = FarApi.PitchUpAxis(v);
                            double ut = Planetarium.GetUniversalTime(), q = v.dynamicPressurekPa;
                            var lift = new Dictionary<int, double>();
                            foreach (int d in new[] { -1, 0, 1 })
                            {
                                // turning the airflow toward the roof lowers the AoA
                                Vector3 vd = Quaternion.AngleAxis(d, axis) * vel;
                                FarApi.ClearMemo(v);
                                if (FarApi.PredictAtFull(v, vd, v.latitude, v.longitude, v.altitude, ut, out Vector3 f, out _))
                                {
                                    LiftDrag(v, f, vd, out double Ld, out _);
                                    lift[d] = Ld;
                                }
                            }
                            FarApi.ClearMemo(v);
                            if (lift.Count == 3)
                            {
                                slope = (lift[-1] - lift[1]) / (2 * Mathf.Deg2Rad * q * area);
                                predL = lift[0];
                            }
                        }
                        double meanAr = MeanAr(v);
                        string k0 = $"{craft}.{phase}.";
                        ctx.Record(k0 + "aoaDeg", string.Format(Inv, "{0:F3} ± {1:F3}", aoa, aoaSd));
                        ctx.Record(k0 + "pitchInput", Math.Round(M("input"), 4));
                        ctx.Record(k0 + "CL", Math.Round(M("CL"), 4));
                        ctx.Record(k0 + "LD", Math.Round(M("LD"), 2));
                        ctx.Record(k0 + "liftSlopePerRad", Math.Round(slope, 3));
                        ctx.Record(k0 + "meanAR", Math.Round(meanAr, 3));
                        abnormal |= !steady || aoaSd > 0.5 || Math.Abs(M("input")) > 0.9;
                        summary.Append(string.Join(",", Csv(craft), phase, steady ? "1" : "0", F(settle, "F1"), F(M("alt"), "F1"), F(M("speed"), "F2"),
                            F(M("mach"), "F4"), F(M("q"), "F3"), F(M("mass"), "F3"), F(area, "F3"), F(aoa, "F4"), F(aoaSd, "F4"), F(Mx("aoa"), "F4"),
                            F(M("input"), "F5"), F(Mx("input"), "F5"), F(M("vs"), "F3"), F(cols.TryGetValue("speed", out var sl) && sl.Count > 0 ? sl.Min() : double.NaN, "F2"),
                            F(M("L"), "F3"), F(M("D"), "F3"), F(M("Lw"), "F3"), F(M("Lb"), "F3"), F(M("CL"), "F5"), F(M("CD"), "F5"), F(M("LD"), "F3"),
                            F(M("LW"), "F4"), F(M("g"), "F3"), F(Mx("g"), "F3"), F(slope, "F4"), F(predL, "F3"), F(meanAr, "F3"))).Append('\n');

                        ctx.Step($"{craft}: {phase} aero visualization shots");
                        yield return VizShots(ctx, FlightGlobals.ActiveVessel, craft.Replace(' ', '_') + (ph > 0 ? "_" + phase : ""), tints, clSat, arrows);
                        File.WriteAllText(Path.Combine(ctx.ResultsDir, "ar-summary.csv"), summary.ToString());
                    }

                    v = FlightGlobals.ActiveVessel;
                    if (broken)
                    {
                        ctx.Record(craft + ".broken", v == null ? "no vessel" : $"{parts - v.parts.Count} parts lost");
                        // the aftermath on the video too
                        yield return Wait.Seconds(3);
                    }
                    if (v != null) Aa.Off(v);
                    rec?.Stop(broken || abnormal, ctx, craft);
                    rec = null;
                    File.WriteAllText(Path.Combine(ctx.ResultsDir, "ar-summary.csv"), summary.ToString());
                }
            }
            finally
            {
                rec?.Stop(true, ctx, "interrupted");
                FarFlightProbe.ResumeOtherPredictors(trajectories);
                File.WriteAllText(Path.Combine(ctx.ResultsDir, "ar-cruise.csv"), csv.ToString());
                File.WriteAllText(Path.Combine(ctx.ResultsDir, "ar-parts.csv"), partsCsv.ToString());
                File.WriteAllText(Path.Combine(ctx.ResultsDir, "ar-summary.csv"), summary.ToString());
            }
        }

        private static string F(double x, string fmt) => x.ToString(fmt, Inv);

        private static string Csv(string s) => s.Contains(',') ? "\"" + s + "\"" : s;

        private static bool IsFarWing(PartModule m)
        {
            for (Type t = m.GetType(); t != null && t != typeof(PartModule); t = t.BaseType)
                if (t.Name == "FARWingAerodynamicModel") return true;
            return false;
        }

        private static bool IsShielded(PartModule m) =>
            m.GetType().GetField("isShielded", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)?.GetValue(m) is bool b && b;

        private static double Num(PartModule m, string field)
        {
            try { return Convert.ToDouble(Reflect.Get(m, field), Inv); }
            catch (Exception) { return double.NaN; }
        }

        // FAR's wing parts' area (m^2), control surfaces in: the reference area the CL and lift slope are on
        private static double WingArea(Vessel v)
        {
            double s = 0;
            foreach (Part p in v.parts)
                foreach (PartModule m in p.Modules)
                    if (IsFarWing(m))
                        s += Num(m, "S");
            return s;
        }

        // the unshielded wing parts' effective aspect ratio, area-weighted
        private static double MeanAr(Vessel v)
        {
            double num = 0, den = 0;
            foreach (Part p in v.parts)
                foreach (PartModule m in p.Modules)
                    if (IsFarWing(m) && !IsShielded(m))
                    {
                        double s = Num(m, "S"), ar = Num(m, "effective_AR");
                        if (double.IsNaN(s) || double.IsNaN(ar)) continue;
                        num += s * ar;
                        den += s;
                    }
            return den > 0 ? num / den : double.NaN;
        }

        // every part's FAR force: the wing model's (kind wing or control) and the voxel model's (kind body, on a wing's
        // part too) apart; tintCl is what FAR's Cl tint shows, the force across the airflow over q and the area (the
        // wing's S, a body's projected area)
        private static void PartForces(Vessel v, string craft, string phase, StringBuilder csv)
        {
            Vector3 vel = v.srf_velocity;
            double q = v.dynamicPressurekPa;
            foreach (Part p in v.parts)
                foreach (PartModule m in p.Modules)
                {
                    string kind;
                    Vector3 f;
                    double area;
                    if (IsFarWing(m))
                    {
                        kind = m.GetType().Name == "FARControllableSurface" ? "control" : "wing";
                        f = IsShielded(m) ? Vector3.zero : (Vector3)Reflect.Get(m, "worldSpaceForce");
                        area = Num(m, "S");
                    }
                    else if (m.GetType().Name == "FARAeroPartModule")
                    {
                        kind = "body";
                        f = (Vector3)Reflect.Get(m, "worldSpaceAeroForce");
                        object pa = Reflect.Get(m, "ProjectedAreas");
                        area = pa != null ? Convert.ToDouble(Reflect.Get(pa, "totalArea"), Inv) : double.NaN;
                    }
                    else
                        continue;
                    LiftDrag(v, f, vel, out double lift, out double drag);
                    double cross = Vector3.ProjectOnPlane(f, vel.normalized).magnitude;
                    bool wing = kind != "body";
                    csv.Append(string.Join(",", Csv(craft), phase, Csv(p.partInfo.title), p.persistentId.ToString(Inv), kind, F(area, "F4"),
                        wing ? F(Num(m, "effective_AR"), "F4") : "", wing ? F(Num(m, "Cl"), "F5") : "", wing ? F(Num(m, "Cd"), "F5") : "",
                        wing ? F(Num(m, "liftslope"), "F4") : "", wing ? F(Num(m, "stall"), "F4") : "",
                        F(lift, "F4"), F(drag, "F4"), F(cross, "F4"), F(cross / (q * area), "F4"), wing && IsShielded(m) ? "1" : "0")).Append('\n');
                }
        }

        // FAR's force in the last physics frame (kN, world): the wing models' (shielded ones apply nothing) and the voxel
        // parts' apart
        private static bool Forces(Vessel v, out Vector3 wing, out Vector3 body)
        {
            wing = body = Vector3.zero;
            if (v == null) return false;
            int n = 0;
            foreach (Part p in v.parts)
                foreach (PartModule m in p.Modules)
                {
                    try
                    {
                        if (IsFarWing(m))
                        {
                            if (IsShielded(m)) continue;
                            wing += (Vector3)Reflect.Get(m, "worldSpaceForce");
                            n++;
                        }
                        else if (m.GetType().Name == "FARAeroPartModule")
                        {
                            body += (Vector3)Reflect.Get(m, "worldSpaceAeroForce");
                            n++;
                        }
                    }
                    catch (Exception) { }
                }
            return n > 0;
        }

        // lift across the airflow toward the vessel's roof and drag against it
        private static void LiftDrag(Vessel v, Vector3 f, Vector3 vel, out double lift, out double drag)
        {
            Vector3 velN = vel.normalized;
            Vector3 roof = -v.ReferenceTransform.forward;
            drag = -Vector3.Dot(f, velN);
            lift = Vector3.Dot(f, Vector3.ProjectOnPlane(roof, velN).normalized);
        }

        // FAR's aero visualization for the vessel (its FlightGUI vessel module's AeroVizGUI), null without it
        private static object AeroViz(Vessel v)
        {
            Type t = Reflect.FindType("FerramAerospaceResearch.FARGUI.FARFlightGUI.FlightGUI");
            if (t == null || v == null) return null;
            Component gui = v.GetComponent(t);
            return gui != null ? Reflect.Get(gui, "AeroVizGUI") : null;
        }

        // Each tint in turn (none, or FAR's Cl, Cd or stall tint alone; FAR's lift and drag arrows with arrows), the UI
        // and the harness's overlay hidden, the vessel shot from above, the side and
        // behind at a distance sized to it: ar_<name>_<tint>_<view>.png in the results.
        private static IEnumerator VizShots(TestContext ctx, Vessel v, string name, string[] tints, double clSat, bool arrows)
        {
            FlightCamera cam = FlightCamera.fetch;
            object viz = AeroViz(v);
            if (cam == null || v == null) yield break;
            if (viz == null) ctx.Record(name + ".aeroViz", "FAR's AeroVizGUI not found: shots untinted");
            float size = 0;
            foreach (Part p in v.parts)
                size = Math.Max(size, 2 * (p.transform.position - v.CoM).magnitude);
            GameEvents.onHideUI.Fire();
            TestRunner.HideOverlay = true;
            bool arrowsWas = PhysicsGlobals.AeroForceDisplay;
            PhysicsGlobals.AeroForceDisplay = arrows;
            try
            {
                if (viz != null) Reflect.Set(viz, "FullySaturatedCl", clSat);
                foreach (string tint in tints)
                {
                    if (viz != null)
                    {
                        Reflect.Set(viz, "TintForCl", tint == "cl");
                        Reflect.Set(viz, "TintForCd", tint == "cd");
                        Reflect.Set(viz, "TintForStall", tint == "stall");
                    }
                    // the camera's heading is from north, and every flight here is on course 90 (east): 3.14 looks from the
                    // south at its right side, 1.57 from behind
                    foreach (var (view, pitch, hdg, dist) in new[] { ("above", 1.35f, 0f, 1.6f), ("side", 0.12f, 3.1416f, 1.5f), ("rear", 0.5f, 1.5708f, 1.5f) })
                    {
                        cam.camPitch = pitch;
                        cam.camHdg = hdg;
                        cam.SetDistance(Math.Max(20f, dist * size));
                        for (int i = 0; i < 10; ++i) yield return new WaitForEndOfFrame();
                        ScreenCapture.CaptureScreenshot(Path.Combine(ctx.ResultsDir, $"ar_{name}_{tint}_{view}.png"));
                        yield return new WaitForEndOfFrame();
                        yield return new WaitForEndOfFrame();
                    }
                }
            }
            finally
            {
                if (viz != null)
                {
                    Reflect.Set(viz, "TintForCl", false);
                    Reflect.Set(viz, "TintForCd", false);
                    Reflect.Set(viz, "TintForStall", false);
                }
                PhysicsGlobals.AeroForceDisplay = arrowsWas;
                TestRunner.HideOverlay = false;
                GameEvents.onShowUI.Fire();
            }
        }

        /// <summary>
        ///     ffmpeg recording the KSP window (gdigrab) to an mp4 until stopped; Stop keeps the file or deletes it
        /// </summary>
        private sealed class Recorder
        {
            private System.Diagnostics.Process proc;
            private string path;

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct Rect { public int Left, Top, Right, Bottom; }

            [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
            private struct Point { public int X, Y; }

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern bool GetClientRect(IntPtr hWnd, out Rect rect);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern bool ClientToScreen(IntPtr hWnd, ref Point point);

            [System.Runtime.InteropServices.DllImport("user32.dll")]
            private static extern IntPtr GetActiveWindow();

            [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
            private static extern IntPtr FindWindow(string className, string windowName);

            // KSP's client area on the desktop: "-offset_x X -offset_y Y -video_size WxH " (even), "" if not found.
            // gdigrab's window capture (title=) gets a stale frame of a Direct3D window; the desktop's pixels are current.
            private static string ClientArea()
            {
                try
                {
                    // Unity's window: the main thread's active window, else the player's window class
                    IntPtr hwnd = GetActiveWindow();
                    if (hwnd == IntPtr.Zero)
                        hwnd = FindWindow("UnityWndClass", null);
                    if (hwnd == IntPtr.Zero)
                        hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                    if (hwnd == IntPtr.Zero || !GetClientRect(hwnd, out Rect r))
                        return "";
                    var p = new Point();
                    if (!ClientToScreen(hwnd, ref p))
                        return "";
                    int w = (r.Right - r.Left) & ~1, h = (r.Bottom - r.Top) & ~1;
                    return w > 0 && h > 0 ? $"-offset_x {p.X} -offset_y {p.Y} -video_size {w}x{h} " : "";
                }
                catch (Exception)
                {
                    return "";
                }
            }

            public static Recorder Start(string ffmpeg, string path)
            {
                string area = ClientArea();
                Debug.Log("[KspTest] recording " + (area.Length > 0 ? area : "the whole desktop ") + "to " + path);
                var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg,
                    "-y -loglevel error -f gdigrab -framerate 30 -draw_mouse 0 " + area + "-i desktop " +
                    "-vf crop=trunc(iw/2)*2:trunc(ih/2)*2 -c:v libx264 -preset ultrafast -crf 26 -pix_fmt yuv420p \"" + path + "\"")
                {
                    UseShellExecute = false, RedirectStandardInput = true, CreateNoWindow = true
                };
                try
                {
                    return new Recorder { proc = System.Diagnostics.Process.Start(psi), path = path };
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[KspTest] ffmpeg did not start: " + e.Message);
                    return null;
                }
            }

            public void Stop(bool keep, TestContext ctx, string craft)
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        proc.StandardInput.Write("q");
                        proc.StandardInput.Flush();
                        if (!proc.WaitForExit(15000))
                            proc.Kill();
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[KspTest] ffmpeg stop: " + e.Message);
                }
                bool have = File.Exists(path);
                if (have && !keep)
                    File.Delete(path);
                ctx.Record(craft + ".video", !have ? "not written" : keep ? Path.GetFileName(path) : "normal, deleted");
            }
        }

        private static string MissingParts(string craftPath)
        {
            if (!File.Exists(craftPath))
                return "(no craft file)";
            var missing = new HashSet<string>();
            foreach (string raw in File.ReadAllLines(craftPath))
            {
                string line = raw.Trim();
                if (!line.StartsWith("part = ", StringComparison.Ordinal))
                    continue;
                string name = line.Substring(7).Trim();
                int id = name.LastIndexOf('_');
                if (id > 0)
                    name = name.Substring(0, id);
                if (PartLoader.getPartInfoByName(name) == null && PartLoader.getPartInfoByName(name.Replace('.', '_')) == null)
                    missing.Add(name);
            }
            return string.Join(" ", missing);
        }

        // A longitude on the equator in the afternoon (the sun 30 deg west of overhead), the first one east of that whose
        // terrain is at least 1500 m below altM.
        private static double DaylightLon(CelestialBody body, double altM)
        {
            CelestialBody sun = Planetarium.fetch != null ? Planetarium.fetch.Sun : null;
            if (sun == null || sun == body) return -60;
            double lon = body.GetLongitude(sun.position) + 30;
            for (int i = 0; i < 36; ++i, lon += 10)
                if (body.TerrainAltitude(0, lon) < altM - 1500)
                    return ((lon + 540) % 360) - 180;
            return -60;
        }
    }
}
