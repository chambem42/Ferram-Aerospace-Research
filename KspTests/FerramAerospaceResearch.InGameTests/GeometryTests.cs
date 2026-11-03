using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FerramAerospaceResearch.FARAeroComponents;
using KspTest;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>
    /// FAR's voxel model following the vessel's shape in flight: what a fairing encloses is out of the airflow until the
    /// fairing goes.
    /// </summary>
    public static class GeometryTests
    {
        /// <summary>
        /// The stock ComSat Lx (its launch clamp taken off: crafts/) carries its satellite in a procedural fairing: FAR shields the satellite's parts (no exposed
        /// area, no force) while the fairing is closed, and exposes them once it is jettisoned and the vessel revoxelized.
        /// </summary>
        [UITest(Kind = "Check", Isolated = true, StartsAt = StartState.Flight, Craft = "ComSat Lx Unclamped", CraftDir = "VAB", TimeoutSec = 900)]
        public static IEnumerator Flight_FairingShieldsItsPayloadUntilJettisoned(TestContext ctx)
        {
            yield return Wait.Seconds(3);
            yield return Wait.Until(() => FARAPI.VesselVoxelizationCompletedEver(FlightGlobals.ActiveVessel), 90, "the vessel voxelized", shot: false);
            yield return Wait.Seconds(2);   // FARVesselAero applies the new sections (shielding) in its next FixedUpdates
            Vessel v = FlightGlobals.ActiveVessel;
            Part fairingBase = v.parts.FirstOrDefault(p => p.Modules.Contains<ModuleProceduralFairing>());
            Assert.NotNull(fairingBase, "the ComSat Lx's fairing");
            var below = new HashSet<Part>();
            Collect(fairingBase, below);
            List<Part> payload = v.parts.Where(p => !below.Contains(p)).ToList();
            Part core = v.rootPart;
            Assert.True(payload.Contains(core), "the probe core is in the payload");
            int shielded = payload.Count(p => p.ShieldedFromAirstream);
            ctx.Record("payload", $"{payload.Count} parts, {shielded} shielded: {string.Join(", ", payload.Where(p => p.ShieldedFromAirstream).Select(p => p.partInfo.name))}");
            Assert.True(core.ShieldedFromAirstream, "the probe core inside the closed fairing is shielded");
            Assert.True(shielded * 2 >= payload.Count, $"most of the payload is shielded ({shielded} of {payload.Count})");
            Assert.True(core.Modules.GetModule<FARAeroPartModule>().ProjectedAreas.totalArea <= 0, "no exposed area on the shielded core");

            ctx.Step("Jettison the fairing");
            fairingBase.Modules.GetModule<ModuleProceduralFairing>().DeployFairing();
            yield return Wait.Until(() => !core.ShieldedFromAirstream, 30, "the probe core exposed after the fairing goes", shot: false);
            v = FlightGlobals.ActiveVessel;
            int stillShielded = payload.Where(p => p != null && p.vessel == v).Count(p => p.ShieldedFromAirstream);
            ctx.Record("afterJettison", $"{stillShielded} payload parts still shielded");
            Assert.True(core.Modules.GetModule<FARAeroPartModule>().ProjectedAreas.totalArea > 0, "the exposed core has area in the airflow");
            Assert.True(stillShielded < shielded, $"fewer payload parts shielded after the jettison ({stillShielded}, from {shielded})");
        }

        private static void Collect(Part p, HashSet<Part> into)
        {
            into.Add(p);
            foreach (Part c in p.children)
                Collect(c, into);
        }
    }
}
