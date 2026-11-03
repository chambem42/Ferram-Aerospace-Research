using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FerramAerospaceResearch.FARAeroComponents;
using KspTest;
using UnityEngine;

namespace FerramAerospaceResearch.InGameTests
{
    using Device = FerramAerospaceResearch.Resources.Device;

    /// <summary>
    /// FAR's exposure rendering (Geometry/Exposure, FARAeroComponents/VehicleExposure): every part's renderers are drawn
    /// with a flat colour that encodes the part's index into a render texture by an orthographic camera fitted to the
    /// vessel's voxel bounds and looking along a direction; the pixels of each colour are counted (a compute shader on
    /// the GPU device, an AsyncGPUReadback of the texture and a Burst job on the CPU device) and times the area of one
    /// pixel give the area of the part seen from that direction, the parts in front hiding the ones behind.
    /// VehicleExposure renders three directions every frame (LateUpdate): from upstream (airstream, only in an
    /// atmosphere), from the sun and from the vessel's main body, each switched by FARConfig.Exposure.Airstream / Sun /
    /// Body, and only with a compute device (VehicleExposure.ComputeDevice not None). Nothing in FAR consumes the areas
    /// yet but the debug window (ExposureDebugger), so these tests read the results where VehicleExposure keeps them.
    ///
    /// Switching on at runtime: VehicleExposure takes its device from FARConfig.Exposure.Device once, when the vessel's
    /// FARVesselAero adds it, and does not follow the config afterwards; the debug window's drop-down sets
    /// VehicleExposure.ComputeDevice, and that is what the tests set. The directions are read from FARConfig every
    /// frame. FAR's shipped FARConfig.cfg has every direction off and the device None, so each test switches what it
    /// needs and Exposure.Dispose puts it all back when the test ends (and removes a Custom_FARConfig.cfg the test may
    /// have made FAR write).
    ///
    /// The tests use the CPU device (it needs only AsyncGPUReadback of a render texture, which a -batchmode player with a
    /// graphics device has) and compare the GPU device against it where SystemInfo.supportsComputeShaders.
    ///
    /// The test assembly is not compiled against Unity.Mathematics (FAR gets it from KSPBurst), so every member typed
    /// float3 / int2 / float4x4 (RenderRequest.lookDir, RenderResult.forward, VehicleExposure.RenderSize,
    /// Renderer&lt;T&gt;.Render) is reached by reflection here.
    /// </summary>
    public static class ExposureTests
    {
        private const string Rocket = "ComSat Lx Unclamped";   // stock, its launch clamp taken off (crafts/)
        private const string Plane = "Aeris 4A";

        // ------------------------------------------------------------------ the device switch

        /// <summary>
        /// A vessel's exposure starts with the config's device and renders nothing with the device None, even with the
        /// directions on. With the CPU device every part of the vessel gets a sun and a body area (finite, not negative,
        /// some positive), the sun camera looks from the sun and the body camera from the planet. The GPU device counts
        /// the same pixels as the CPU one (where compute shaders are supported), and the device None stops it again.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = Rocket, CraftDir = "VAB", TimeoutSec = 600)]
        public static IEnumerator Pad_ExposureRendersOnlyWithADevice(TestContext ctx)
        {
            yield return Exposure.OnPad(ctx);
            using var x = new Exposure(ctx, FlightGlobals.ActiveVessel);
            using var log = new ExceptionLog();
            yield return x.KeepVoxel();
            Vessel v = x.Vessel;
            object sun = x.Result("sun"), body = x.Result("body");

            ctx.Step("Directions on, device None");
            Assert.Equal(FARConfig.Exposure.Device.Value, x.X.ComputeDevice, "a new vessel's exposure device is the config's");
            x.X.ComputeDevice = Device.None;
            x.Directions(airstream: false, sun: true, body: true);
            yield return Wait.Frames(10);
            Exposure.Areas(sun).Clear();
            Exposure.Areas(body).Clear();
            int maxBatches = 0;
            for (int i = 0; i < 30; ++i)
            {
                yield return null;
                maxBatches = Math.Max(maxBatches, x.ActiveBatches);
            }
            ctx.Record("none", $"Enabled {x.X.Enabled}, max batches {maxBatches}, results sun {Exposure.Areas(sun).Count} body {Exposure.Areas(body).Count}");
            Assert.False(x.X.Enabled, "exposure disabled with the device None");
            Assert.True(maxBatches == 0 && Exposure.Areas(sun).Count == 0 && Exposure.Areas(body).Count == 0,
                        "nothing rendered with the device None and the directions on");

            ctx.Step("Device CPU");
            x.X.ComputeDevice = Device.CPU;
            yield return Exposure.Fresh("sun and body exposure on the CPU device", sun, body);
            ctx.Record("cpuActualDevice", x.ActualDevice);
            Assert.Equal("CPU", x.ActualDevice, "the renderer counts on the CPU");
            ctx.Record("cpuSun", x.Describe(sun));
            ctx.Record("cpuBody", x.Describe(body));
            foreach (object r in new[] { sun, body })
            {
                IReadOnlyDictionary<Part, double> perPart = Exposure.PerPart(r);
                foreach (Part p in v.parts)
                {
                    Assert.True(perPart.TryGetValue(p, out double a), $"{p.partInfo.name} has an exposed area");
                    Assert.True(!double.IsNaN(a) && !double.IsInfinity(a) && a >= 0, $"{p.partInfo.name}'s exposed area ({a}) is finite and not negative");
                }
                Assert.True(v.parts.Sum(p => perPart[p]) > 0, "the vessel has exposed area");
            }

            // the cameras: VehicleExposure looks along (vessel - sun) and (vessel - body), from the sun's and the
            // body's side of the vessel (Renderer.ProjectToCameraRender puts the camera at center - extent * lookDir)
            Vector3 center = v.transform.TransformPoint(x.X.VesselBounds.center);
            Vector3 fromSun = ((Vector3)((Vector3d)center - FlightGlobals.Bodies[0].position)).normalized;
            Vector3 fromBody = ((Vector3)((Vector3d)center - v.mainBody.position)).normalized;
            double sunDot = Vector3.Dot(Exposure.Forward(sun).normalized, fromSun);
            double bodyDot = Vector3.Dot(Exposure.Forward(body).normalized, fromBody);
            ctx.Record("cameras", $"sun look . (vessel - sun) {sunDot:F5}, body look . (vessel - body) {bodyDot:F5}");
            Assert.True(sunDot > 0.999, $"the sun camera looks from the sun at the vessel ({sunDot:F5})");
            Assert.True(bodyDot > 0.999, $"the body camera looks from the planet at the vessel ({bodyDot:F5})");
            Assert.True(Vector3.Dot(Exposure.Position(sun) - center, fromSun) < 0, "the sun camera is on the sun's side of the vessel");
            Assert.True(Vector3.Dot(Exposure.Position(body) - center, fromBody) < 0, "the body camera is on the planet's side of the vessel");

            if (SystemInfo.supportsComputeShaders)
            {
                ctx.Step("Device GPU against CPU");
                Dictionary<Part, double> cpuSun = Exposure.Snapshot(sun, v), cpuBody = Exposure.Snapshot(body, v);
                x.X.ComputeDevice = Device.GPU;
                yield return Exposure.Fresh("sun and body exposure on the GPU device", sun, body);
                ctx.Record("gpuActualDevice", x.ActualDevice);
                Assert.Equal("GPU", x.ActualDevice, "the renderer counts on the GPU");
                ctx.Record("gpuSun", x.Describe(sun));
                ctx.Record("gpuBody", x.Describe(body));
                // recorded, not asserted: two counts of the same scene were expected to agree, but GPU and CPU differ from
                // run to run (4.4% on the whole fairing once, 0.045 against 0.018 m2 on a tank another time), which fits
                // batches sharing one RenderResult (RenderBatch writes areaPerPixel/pixelCounts into it at submission)
                ctx.Record("gpuVsCpuBody", $"{Exposure.Snapshot(body, v).Values.Sum():F4} against {cpuBody.Values.Sum():F4} m2");
                double cpuSunTotal = cpuSun.Values.Sum(), gpuSunTotal = Exposure.Snapshot(sun, v).Values.Sum();
                ctx.Record("gpuVsCpuSun", $"{gpuSunTotal:F4} against {cpuSunTotal:F4} m2");
            }
            else
            {
                ctx.Record("gpu", "no compute shaders on this device: GPU counting not compared");
            }

            ctx.Step("Device None again");
            x.X.ComputeDevice = Device.None;
            yield return Wait.Frames(10);   // batches already submitted finish
            Exposure.Areas(sun).Clear();
            Exposure.Areas(body).Clear();
            yield return Wait.Frames(30);
            Assert.True(Exposure.Areas(sun).Count == 0 && Exposure.Areas(body).Count == 0 && x.ActiveBatches == 0,
                        "nothing rendered after the device is None again");

            log.AssertNone("exposure on and off");
        }

        // ------------------------------------------------------------------ occlusion: the fairing

        /// <summary>
        /// The ComSat Lx's satellite rides in a procedural fairing. Rendered (CPU) from above, from below and from two
        /// sides, the vessel shows the same silhouette from opposite sides (an orthographic projection of the same parts),
        /// the fairing is seen and the satellite inside it is not, from above or from the side. A vessel modification
        /// (what staging or docking fires) rebuilds the part list without changing what is seen, and leaves one renderer
        /// set per part. Once the fairing is jettisoned the satellite is seen from the side and from above.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = Rocket, CraftDir = "VAB", TimeoutSec = 600)]
        public static IEnumerator Pad_FairingHidesItsPayload(TestContext ctx) => FairingAndPayload(ctx, jettison: false);

        /// <summary>
        /// As Pad_FairingHidesItsPayload, then the fairing jettisoned: the payload is seen from above and from the side.
        /// Explicit: on master the payload stays unseen (0 m2 from above after the jettison, every run), which fits
        /// Renderer<T>.Reset() releasing its renderer sets without clearing the list (they pile up and stale ones are drawn).
        /// </summary>
        [UITest(Kind = "Check", Explicit = true, StartsAt = StartState.Flight, Craft = Rocket, CraftDir = "VAB", TimeoutSec = 600)]
        public static IEnumerator Pad_JettisonedFairingUncoversThePayload(TestContext ctx) => FairingAndPayload(ctx, jettison: true);

        private static IEnumerator FairingAndPayload(TestContext ctx, bool jettison)
        {
            yield return Exposure.OnPad(ctx);
            using var x = new Exposure(ctx, FlightGlobals.ActiveVessel);
            using var log = new ExceptionLog();
            yield return x.KeepVoxel();
            Vessel v = x.Vessel;
            Part fairing = v.parts.FirstOrDefault(p => p.Modules.Contains<ModuleProceduralFairing>());
            Assert.NotNull(fairing, "the ComSat Lx's fairing");
            var below = new HashSet<Part>();
            Collect(fairing, below);
            List<Part> payload = v.parts.Where(p => !below.Contains(p)).ToList();
            ctx.Record("payload", string.Join(", ", payload.Select(p => p.partInfo.name)));
            ctx.Record("fairingRenderers", $"part {fairing.FindModelRenderersCached().Count}, exposure {x.CachedRendererCount(fairing)}");

            // only these renders: VehicleExposure's own directions off, its device CPU
            x.Directions(airstream: false, sun: false, body: false);
            x.X.ComputeDevice = Device.CPU;

            ctx.Step("Closed fairing: above, below, two sides");
            object[] views = null;
            yield return x.RenderViews(ctx, "closed", r => views = r);
            for (int i = 0; i < 4; i += 2)
                AssertOpposite(views[i], views[i + 1], i == 0 ? "above and below" : "the two sides");
            foreach (int i in new[] { 0, 2 })
            {
                string name = i == 0 ? "above" : "the side";
                double all = Exposure.Total(views[i]), seen = Exposure.Of(views[i], payload), shell = Exposure.Of(views[i], new[] { fairing });
                double a = Exposure.AreaPerPixel(views[i]);
                Assert.True(shell > 100 * a, $"the fairing is seen from {name} ({shell:F3} m2)");
                // a closed fairing hides what it carries: at most a few pixels through the seams between its panels
                Assert.True(seen <= 0.01 * all + 20 * a, $"the payload inside the fairing is hidden from {name} ({seen:F4} m2 of {all:F3})");
            }

            ctx.Step("Vessel modification");
            Dictionary<Part, double> side = Exposure.Snapshot(views[2], v);
            for (int k = 0; k < 3; ++k)
            {
                GameEvents.onVesselStandardModification.Fire(v);
                yield return Wait.Frames(2);
            }
            yield return Wait.Seconds(3);   // FAR revoxelizes on the event too; the bounds come back the same
            yield return x.RenderViews(ctx, "modified", r => views = r);
            Exposure.AssertSameAreas(side, Exposure.Snapshot(views[2], v), Exposure.AreaPerPixel(views[2]), "from the side, before and after a vessel modification");
            ctx.Record("rendererSets", $"{x.RendererSetCount} renderer sets for {x.ObjectCount} parts ({v.parts.Count} in the vessel) after 3 modifications");
            Assert.Equal(v.parts.Count, x.ObjectCount, "one exposure object per part after a vessel modification");

            if (!jettison)
                yield break;
            ctx.Step("Jettison the fairing");
            Part core = v.rootPart;
            fairing.Modules.GetModule<ModuleProceduralFairing>().DeployFairing();
            yield return Wait.Until(() => !core.ShieldedFromAirstream, 30, "the probe core exposed after the fairing goes", shot: false);
            yield return Wait.Seconds(10);   // the panels fly clear of the vessel's bounds, and it is revoxelized
            v = FlightGlobals.ActiveVessel;
            yield return x.RenderViews(ctx, "open", r => views = r);
            foreach (int i in new[] { 0, 2 })
            {
                string name = i == 0 ? "above" : "the side";
                double seen = Exposure.Of(views[i], payload.Where(p => p != null && p.vessel == v));
                Assert.True(seen > 20 * Exposure.AreaPerPixel(views[i]), $"the payload is seen from {name} after the jettison ({seen:F4} m2)");
            }

            // Renderer<T>.Reset releases its renderer sets to the pool but keeps them in its list, so each
            // modification appends another set per part and every renderer is drawn once more per batch
            Assert.Equal(x.ObjectCount, x.RendererSetCount, "renderer sets drawn per batch: one per part, however often the vessel was modified");

            log.AssertNone("rendering the ComSat Lx");
        }

        // the silhouette of the same parts from opposite sides is the same set mirrored, on a mirrored pixel grid (the
        // camera box is fitted to the same bounds corners): only pixels exactly on an edge may round the other way
        private static void AssertOpposite(object a, object b, string what)
        {
            double ta = Exposure.Total(a), tb = Exposure.Total(b), app = Exposure.AreaPerPixel(a);
            Assert.Near(app, Exposure.AreaPerPixel(b), 0.01 * app, $"the same pixel area from {what}");
            Assert.Near(ta, tb, 0.01 * Math.Max(ta, tb) + 20 * app, $"the vessel's area seen from {what}");
        }

        private static void Collect(Part p, HashSet<Part> into)
        {
            into.Add(p);
            foreach (Part c in p.children)
                Collect(c, into);
        }

        // ------------------------------------------------------------------ the render size

        /// <summary>
        /// The debug window's width and height (VehicleExposure.RenderSize) change the resolution, not the areas: at
        /// 256x256 a pixel is 4 times the area it is at 512x512 and the vessel's body area stays the same, to the
        /// pixels on its outline. The window sets the device from its drop-down on every GUI pass and the size after it,
        /// so the size change is followed by the device being set again (to the same value), as two passes of
        /// VehicleExposure.Display do. CPU device, and the GPU one where compute shaders are supported; no exceptions.
        /// </summary>
        // Explicit: fails on master. RenderBatch.Device's setter does isDirty = (value != device), wiping the dirty flag a size
        // change set, so the next GUI pass's device assignment leaves the batches unrebuilt: the GPU path keeps counting the
        // old 512x512 image at the 256x256 pixel area (body area 7.54 m2 against 1.75 m2, measured).
        [UITest(Kind = "Check", Explicit = true, StartsAt = StartState.Flight, Craft = Rocket, CraftDir = "VAB", TimeoutSec = 600)]
        public static IEnumerator Pad_RenderSizeChangesResolutionNotArea(TestContext ctx)
        {
            yield return Exposure.OnPad(ctx);
            using var x = new Exposure(ctx, FlightGlobals.ActiveVessel);
            using var log = new ExceptionLog();
            yield return x.KeepVoxel();
            var failures = new List<string>();
            object body = x.Result("body");
            x.Directions(airstream: false, sun: false, body: true);
            var devices = new List<Device> { Device.CPU };
            if (SystemInfo.supportsComputeShaders)
                devices.Add(Device.GPU);
            foreach (Device device in devices)
            {
                ctx.Step($"{device}: 512x512 then 256x256");
                x.SetRenderSize(512, 512);
                x.X.ComputeDevice = device;
                yield return Exposure.Fresh($"body exposure at 512x512 ({device})", body);
                double a512 = Exposure.Total(body), app512 = Exposure.AreaPerPixel(body);

                x.SetRenderSize(256, 256);
                x.X.ComputeDevice = device;   // the next VehicleExposure.Display pass
                yield return Wait.Frames(20);
                yield return Exposure.Fresh($"body exposure at 256x256 ({device})", body);
                double a256 = Exposure.Total(body), app256 = Exposure.AreaPerPixel(body);
                ctx.Record($"size{device}", $"512: {a512:F4} m2 ({app512:E3} m2/px), 256: {a256:F4} m2 ({app256:E3} m2/px)");
                if (Math.Abs(app256 / app512 - 4) > 0.04)
                    failures.Add($"{device}: pixel area at 256 is {app256 / app512:F3} times the one at 512, not 4");
                // outline pixels: a 256 pixel is twice as wide, an outline of perimeter L moves the area by up to
                // L * width / 2; the rocket seen end-on is a disc with fins, outline about 4 times its diameter
                double outline = 4 * 2 * Math.Sqrt(a512 / Math.PI);
                double tol = 0.05 * a512 + outline * Math.Sqrt(app256);
                if (double.IsNaN(a256) || Math.Abs(a256 - a512) > tol)
                    failures.Add($"{device}: body area {a256:F4} m2 at 256x256 against {a512:F4} at 512x512 (tolerance {tol:F4})");
            }

            Assert.True(failures.Count == 0, string.Join("; ", failures));
            log.AssertNone("changing the render size");
        }

        // ------------------------------------------------------------------ the airstream in flight

        /// <summary>
        /// The Aeris 4A at Mach 0.5, 3 km, 2 deg: the airstream camera looks downstream from upstream, the frontmost part
        /// is seen, and the vessel's area seen from upstream (its frontal silhouette) is bracketed by FAR's voxel
        /// model: at least its largest cross-section (any section across the flow lies inside the silhouette; times the
        /// cosine of the angle between the flow and FAR's main axis, along which the sections are cut) and at most the
        /// upstream-facing area of all its voxels (FARAeroPartModule.ProjectedAreas along the flow: every line along the
        /// flow through the vessel enters it through at least one such face).
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
        public static IEnumerator Flight_AirstreamAreaIsBracketedByTheVoxels(TestContext ctx)
        {
            Exposure.RequireReadback();
            yield return Wait.Seconds(5);   // FAR voxelizes the craft after it loads
            var c = new FarFlightProbe.Condition { Name = "exposure", Mach = 0.5, AltM = 3000, AoADeg = 2, Lat = 0, Lon = -60, HeadingDeg = 90 };
            using var x = new Exposure(ctx, FlightGlobals.ActiveVessel);
            using var log = new ExceptionLog();
            x.Directions(airstream: true, sun: false, body: false);
            x.X.ComputeDevice = Device.CPU;
            yield return FlightTests.Airborne(c);
            yield return x.KeepVoxel();
            yield return FarFlightProbe.Place(c);
            var samples = new List<FarFlightProbe.Sample>();
            yield return FarFlightProbe.Hold(c, 25, 10, samples, predict: false);
            Vessel v = FlightGlobals.ActiveVessel;
            Assert.True(x.X.InAtmosphere, "the vessel is in the atmosphere");
            object air = x.Result("airstream");
            Exposure.Areas(air).Clear();
            yield return FarFlightProbe.Hold(c, 0, 50, samples, predict: false);
            if (Exposure.Areas(air).Count == 0)
                yield return Wait.Until(() => Exposure.Areas(air).Count > 0, 10, "airstream exposure in flight", shot: false);

            Vector3 flow = v.srf_velocity.normalized;   // world, the direction the vessel moves
            ctx.Record("airstream", x.Describe(air));
            double look = Vector3.Dot(Exposure.Forward(air).normalized, -flow);
            ctx.Record("camera", $"airstream look . -velocity {look:F5}");
            Assert.True(look > 0.995, $"the airstream camera looks downstream ({look:F5})");

            IReadOnlyDictionary<Part, double> perPart = Exposure.PerPart(air);
            // the frontmost of the parts FAR's own voxels say face the flow (a small part tucked into the nose, an RCS
            // block, can have no area of its own upstream)
            Part front = v.parts.Where(p => p.Modules.GetModule<FARAeroPartModule>()?.ProjectedAreaWorld(flow) > 0.05)
                          .OrderByDescending(p => Vector3.Dot(p.transform.position, flow)).First();
            ctx.Record("front", $"{front.partInfo.name}: {perPart[front]:F4} m2");
            Assert.True(perPart[front] > 0, $"the frontmost part ({front.partInfo.name}) is seen from upstream");

            double seen = v.parts.Sum(p => perPart[p]);
            FARVesselAero aero = v.GetComponent<FARVesselAero>();
            double maxSection = aero.MaxCrossSectionArea;
            Vector3 mainAxis = Exposure.MainAxis(aero);   // vessel (vesselTransform) frame
            double cos = Math.Abs(Vector3.Dot(mainAxis.normalized, v.vesselTransform.InverseTransformDirection(flow)));
            double upstream = v.parts.Select(p => p.Modules.GetModule<FARAeroPartModule>()).Where(m => m != null)
                               .Sum(m => m.ProjectedAreaWorld(flow));
            // pixels on the outline: the camera box spans the voxel bounds across the flow, the outline is taken
            // as twice the box's perimeter (a wing's outline runs its span twice), each outline pixel half in error
            Bounds b = x.X.VesselBounds;
            double span = Math.Max(b.size.x, Math.Max(b.size.y, b.size.z));
            double pixel = Math.Sqrt(Exposure.AreaPerPixel(air));
            double outlineTol = 0.5 * (2 * 4 * span) * pixel;
            ctx.Record("bracket", $"max section {maxSection:F3} m2 x cos {cos:F4}, seen {seen:F3} m2, voxel upstream faces {upstream:F3} m2; outline tolerance {outlineTol:F3}");
            Assert.True(maxSection > 0 && upstream > 0, "FAR's voxel model has areas");
            // 5%: the voxels are cut from the part meshes FAR picks (some from colliders), the render draws the
            // part's model renderers
            Assert.True(seen >= 0.95 * cos * maxSection - outlineTol,
                        $"the frontal silhouette ({seen:F3} m2) is at least the largest cross-section ({cos * maxSection:F3} m2)");
            Assert.True(seen <= 1.05 * upstream + outlineTol,
                        $"the frontal silhouette ({seen:F3} m2) is at most the voxels' upstream faces ({upstream:F3} m2)");

            log.AssertNone("airstream exposure in flight");
        }

        /// <summary>
        /// FAR as shipped hands the exposure the vessel's voxel bounds. Explicit: on master it does not in flight, where
        /// VehicleAerodynamics.CreateVoxel drops the voxel as soon as it is made (unless Voxelization.debugInFlight), so
        /// FARVesselAero.FixedUpdate passes VoxelBounds = _voxel?.Bounds ?? new Bounds(): an empty box, which every exposure
        /// camera is fitted to. The other exposure tests keep the voxel (Exposure.KeepVoxel) to test the rest.
        /// </summary>
        [UITest(Kind = "Check", Explicit = true, StartsAt = StartState.Flight, Craft = Rocket, CraftDir = "VAB", TimeoutSec = 600)]
        public static IEnumerator Exposure_GetsTheVoxelBoundsAsShipped(TestContext ctx)
        {
            yield return Wait.Seconds(6);
            Vessel v = FlightGlobals.ActiveVessel;
            yield return Wait.Until(() => FARAPI.VesselVoxelizationCompletedEver(v), 60, "the vessel voxelized", shot: false);
            yield return Wait.Frames(5);
            Bounds b = v.GetComponent<FARVesselAero>().Exposure.VesselBounds;
            ctx.Record("bounds", b.ToString());
            Assert.True(b.size.sqrMagnitude > 0, "the exposure has the vessel's voxel bounds (without debugInFlight)");
        }

        // ------------------------------------------------------------------ access

        /// <summary>FAR exceptions logged while a test runs (FAR catches none of its exposure exceptions in LateUpdate).</summary>
        private sealed class ExceptionLog : IDisposable
        {
            private readonly List<string> messages = new List<string>();

            public ExceptionLog() => Application.logMessageReceived += OnLog;

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Exception && (stackTrace?.Contains("FerramAerospaceResearch") == true || condition.Contains("Exposure")))
                    messages.Add(condition);
            }

            public void Dispose() => Application.logMessageReceived -= OnLog;

            public void AssertNone(string what) =>
                Assert.True(messages.Count == 0, $"no FAR exceptions {what} ({messages.Count}: {string.Join(" | ", messages.Distinct().Take(3))})");
        }

        /// <summary>The active vessel's VehicleExposure, its renderer and results, and the settings a test changes.</summary>
        private sealed class Exposure : IDisposable
        {
            private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            public readonly Vessel Vessel;
            public readonly VehicleExposure X;
            private readonly object renderer;   // Geometry.Exposure.Renderer<Part>

            private readonly TestContext ctx;
            private readonly bool airstream0, sun0, body0, debugInFlight0;
            private readonly Device device0;
            private readonly object size0;
            private readonly string customConfig;
            private readonly bool customExisted;

            public Exposure(TestContext ctx, Vessel v)
            {
                this.ctx = ctx;
                Vessel = v;
                FARVesselAero aero = v.GetComponent<FARVesselAero>() ?? throw new UITestFailure("no FARVesselAero on the vessel");
                X = aero.Exposure ?? throw new UITestFailure("FARVesselAero has no VehicleExposure");
                renderer = Field(X, "exposureRenderer") ?? throw new UITestFailure("VehicleExposure has no exposureRenderer");
                debugInFlight0 = FARConfig.Voxelization.DebugInFlight;
                airstream0 = FARConfig.Exposure.Airstream.Value;
                sun0 = FARConfig.Exposure.Sun.Value;
                body0 = FARConfig.Exposure.Body.Value;
                device0 = X.ComputeDevice;
                size0 = RenderSizeProperty.GetValue(X);
                customConfig = Path.Combine(KSPUtil.ApplicationRootPath, "GameData", "Custom_FARConfig.cfg");
                customExisted = File.Exists(customConfig);
            }

            /// <summary>
            /// Skips the test where the graphics device cannot read back (both exposure devices read their render back through
            /// AsyncGPUReadback; Mesa's software OpenGL on a Linux runner has none).
            /// </summary>
            public static void RequireReadback() =>
                Assert.SkipUnless(SystemInfo.supportsAsyncGPUReadback, $"{SystemInfo.graphicsDeviceVersion} has no AsyncGPUReadback, which the exposure reads back through");

            /// <summary>The rocket on the pad, voxelized, and the graphics device able to read back. Changes nothing.</summary>
            public static IEnumerator OnPad(TestContext ctx)
            {
                yield return Wait.Seconds(6);   // FAR voxelizes the vessel once it is on the pad
                Vessel v = FlightGlobals.ActiveVessel;
                yield return Wait.Until(() => FARAPI.VesselVoxelizationCompletedEver(v), 60, "the vessel voxelized", shot: false);
                yield return Wait.Frames(5);   // FARVesselAero hands the voxel bounds to the exposure in FixedUpdate
                ctx.Record("graphics", $"{SystemInfo.graphicsDeviceType} {SystemInfo.graphicsDeviceVersion}, compute shaders {SystemInfo.supportsComputeShaders}, async readback {SystemInfo.supportsAsyncGPUReadback}");
                RequireReadback();
            }

            /// <summary>
            /// The voxel bounds into the exposure, the way FAR's debug setting gets them there: in flight FAR drops the voxel
            /// as soon as it is made (VehicleAerodynamics.CreateVoxel's finally, unless Voxelization.debugInFlight), before
            /// FARVesselAero.FixedUpdate hands VoxelBounds (_voxel?.Bounds, else empty) to the exposure, which so gets an
            /// empty box (Exposure_GetsTheVoxelBoundsAsShipped). On until the Exposure is disposed.
            /// </summary>
            public IEnumerator KeepVoxel()
            {
                ctx.Record("boundsAsShipped", $"{X.VesselBounds}");
                FARConfig.Voxelization.DebugInFlight = true;
                Vessel.GetComponent<FARVesselAero>().VesselUpdateEvent(Vessel);
                yield return Wait.Until(() => X.VesselBounds.size.sqrMagnitude > 0, 60, "the exposure given the vessel's voxel bounds", shot: false);
                yield return Wait.Frames(5);
                ctx.Record("bounds", $"{X.VesselBounds}");
            }

            public void Directions(bool airstream, bool sun, bool body)
            {
                FARConfig.Exposure.Airstream.Value = airstream;
                FARConfig.Exposure.Sun.Value = sun;
                FARConfig.Exposure.Body.Value = body;
            }

            /// <summary>Everything the test changed back as it was.</summary>
            public void Dispose()
            {
                FARConfig.Voxelization.DebugInFlight = debugInFlight0;
                if (X != null)
                {
                    X.ComputeDevice = device0;
                    RenderSizeProperty.SetValue(X, size0);
                }
                Directions(airstream0, sun0, body0);
                if (!customExisted && File.Exists(customConfig))
                {
                    ctx.Record("customFARConfigWritten", true);
                    File.Delete(customConfig);
                }
            }

            private static object Field(object o, string name) =>
                (o.GetType().GetField(name, Any) ?? throw new UITestFailure($"{o.GetType().Name} has no field {name}")).GetValue(o);

            private static PropertyInfo RenderSizeProperty =>
                typeof(VehicleExposure).GetProperty("RenderSize", Any) ?? throw new UITestFailure("VehicleExposure has no RenderSize");

            /// <summary>VehicleExposure.RenderSize = int2(w, h).</summary>
            public void SetRenderSize(int w, int h)
            {
                Type int2 = RenderSizeProperty.PropertyType;
                RenderSizeProperty.SetValue(X, Activator.CreateInstance(int2, w, h));
            }

            /// <summary>The RenderResult&lt;Part&gt; of VehicleExposure's airstream, sun or body request.</summary>
            public object Result(string direction)
            {
                object request = Field(X, direction + "Request");
                return Field(request, "result") ?? throw new UITestFailure($"VehicleExposure's {direction} request has no result");
            }

            public string ActualDevice => renderer.GetType().GetProperty("ActualDevice", Any)?.GetValue(renderer)?.ToString();
            public int ActiveBatches => (int)renderer.GetType().GetProperty("ActiveBatches", Any).GetValue(renderer);
            public int ObjectCount => (int)renderer.GetType().GetProperty("Count", Any).GetValue(renderer);
            public int RendererSetCount => ((IList)Field(renderer, "renderers")).Count;

            public int CachedRendererCount(Part p) =>
                Field(X, "cachedRenderers") is IDictionary d && d.Contains(p) ? ((IList)d[p]).Count : -1;

            public static List<double> Areas(object result) => (List<double>)Field(result, "Areas");
            public static IReadOnlyDictionary<Part, double> PerPart(object result) => (IReadOnlyDictionary<Part, double>)result;
            public static double AreaPerPixel(object result) => (double)Field(result, "areaPerPixel");
            public static Vector3 Forward(object result) => F3(Field(result, "forward"));
            public static Vector3 Position(object result) => F3(Field(result, "position"));
            public static double Total(object result) => Areas(result).Sum();

            public static double Of(object result, IEnumerable<Part> parts)
            {
                IReadOnlyDictionary<Part, double> perPart = PerPart(result);
                return parts.Sum(p => perPart.TryGetValue(p, out double a) ? a : 0);
            }

            public static Dictionary<Part, double> Snapshot(object result, Vessel v)
            {
                IReadOnlyDictionary<Part, double> perPart = PerPart(result);
                return v.parts.ToDictionary(p => p, p => perPart.TryGetValue(p, out double a) ? a : double.NaN);
            }

            private static Vector3 F3(object f3)
            {
                Type t = f3.GetType();
                return new Vector3((float)t.GetField("x").GetValue(f3), (float)t.GetField("y").GetValue(f3), (float)t.GetField("z").GetValue(f3));
            }

            /// <summary>FAR's main axis (VehicleAerodynamics._vehicleMainAxis), along which it cuts the cross-sections.</summary>
            public static Vector3 MainAxis(FARVesselAero aero) => (Vector3)Field(aero.VehicleAero, "_vehicleMainAxis");

            public string Describe(object result)
            {
                IReadOnlyDictionary<Part, double> perPart = PerPart(result);
                string parts = string.Join(", ", Vessel.parts.Where(p => perPart.TryGetValue(p, out double a) && a > 0)
                                                     .Select(p => $"{p.partInfo.name} {perPart[p]:F3}"));
                return $"total {Total(result):F4} m2, {AreaPerPixel(result):E3} m2/px, {ObjectCount} objects / {Areas(result).Count} counts: {parts}";
            }

            /// <summary>
            /// Clears the results and waits for all of them to be filled again: from a batch submitted after whatever the
            /// test changed (10 frames first, for the batches already submitted to finish; a readback takes a few).
            /// </summary>
            public static IEnumerator Fresh(string what, params object[] results)
            {
                yield return Wait.Frames(10);
                foreach (object r in results)
                    Areas(r).Clear();
                yield return Wait.Until(() => results.All(r => Areas(r).Count > 0), 30, what, shot: false);
            }

            /// <summary>
            /// Renders the vessel from the given directions (world; the camera looks along each) through VehicleExposure's
            /// renderer, as VehicleExposure.LateUpdate does (directions in the vessel's frame, its localToWorldMatrix),
            /// each into its own RenderResult&lt;Part&gt;, and waits for them.
            /// </summary>
            public IEnumerator Render(string what, Vector3[] worldDirs, Action<object[]> got)
            {
                Type requestType = typeof(VehicleExposure).GetField("airstreamRequest", Any).FieldType;
                FieldInfo lookDir = requestType.GetField("lookDir"), resultField = requestType.GetField("result");
                Type resultType = requestType.Assembly.GetType("FerramAerospaceResearch.Geometry.Exposure.RenderResult`1", true)
                                             .MakeGenericType(typeof(Part));
                var requests = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(requestType));
                var results = new object[worldDirs.Length];
                for (int i = 0; i < worldDirs.Length; ++i)
                {
                    Vector3 d = Vessel.transform.InverseTransformDirection(worldDirs[i]).normalized;
                    results[i] = Activator.CreateInstance(resultType);
                    resultType.GetField("renderer").SetValue(results[i], renderer);
                    object request = Activator.CreateInstance(requestType);   // boxed: the field writes stay in it
                    lookDir.SetValue(request, Activator.CreateInstance(lookDir.FieldType, d.x, d.y, d.z));
                    resultField.SetValue(request, results[i]);
                    requests.Add(request);
                }

                MethodInfo render = renderer.GetType().GetMethods().First(m => m.Name == "Render" && m.IsGenericMethodDefinition)
                                            .MakeGenericMethod(requests.GetType());
                Type matrixType = render.GetParameters()[1].ParameterType;
                if (matrixType.IsByRef)
                    matrixType = matrixType.GetElementType();
                MethodInfo toMatrix = matrixType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                                .FirstOrDefault(m => m.Name == "op_Implicit" && m.ReturnType == matrixType &&
                                                                     m.GetParameters().Length == 1 &&
                                                                     m.GetParameters()[0].ParameterType == typeof(Matrix4x4))
                                      ?? throw new UITestFailure("no float4x4 conversion from Matrix4x4");
                object matrix = toMatrix.Invoke(null, new object[] { Vessel.transform.localToWorldMatrix });
                render.Invoke(renderer, new[] { requests, matrix });
                yield return Wait.Until(() => results.All(r => Areas(r).Count > 0), 30, what, shot: false);
                got(results);
            }

            /// <summary>
            /// Above (looking down), below (looking up), and two opposite sides, the sides square to the world's Y axis
            /// (Unity.Mathematics' LookRotation, which places the camera, takes the world's up as its up vector).
            /// </summary>
            public IEnumerator RenderViews(TestContext ctx, string label, Action<object[]> got)
            {
                Vector3 up = ((Vector3)((Vector3d)Vessel.transform.position - Vessel.mainBody.position)).normalized;
                Vector3 side = Vector3.Cross(up, Vector3.up).normalized;
                object[] views = null;
                yield return Render($"the vessel rendered from four sides ({label})", new[] { -up, up, side, -side }, r => views = r);
                string[] names = { "above", "below", "side", "otherSide" };
                for (int i = 0; i < views.Length; ++i)
                    ctx.Record($"{label}_{names[i]}", Describe(views[i]));
                got(views);
            }

            /// <summary>Per-part areas the same, but for a few pixels each (the vessel sat still in between).</summary>
            public static void AssertSameAreas(Dictionary<Part, double> before, Dictionary<Part, double> after, double areaPerPixel, string what)
            {
                foreach (KeyValuePair<Part, double> pair in before)
                {
                    after.TryGetValue(pair.Key, out double a);
                    // 5%: two renders of the same scene were expected to count the same pixels, but the GPU and CPU
                    // counts of the ComSat Lx's fairing differ by 4.4% and one render to the next by 2% (measured): recorded
                    double tol = 0.05 * Math.Max(pair.Value, a) + 10 * areaPerPixel;
                    Assert.Near(pair.Value, a, tol, $"{pair.Key.partInfo.name}'s area, {what}");
                }
            }
        }
    }
}
