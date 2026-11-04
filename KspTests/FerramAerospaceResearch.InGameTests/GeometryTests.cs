using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FerramAerospaceResearch.FARAeroComponents;
using FerramAerospaceResearch.FARPartGeometry;
using KspTest;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>
    /// FAR's voxel model following the vessel's shape in flight: what a fairing encloses is out of the airflow until the
    /// fairing goes; a kerbal out on EVA is a box.
    /// </summary>
    public static class GeometryTests
    {
        /// <summary>
        /// A kerbal out on EVA from the stock Kerbal 1 on the pad: FAR gives the kerbal, whose part has no meshes FAR can
        /// use, its geometry and aero modules as it comes out, and models it as a 0.5 x 0.8 x 0.5 m box: 0.8 m long along
        /// its height, 0.25 m2 across, 0.2 m3.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = "Kerbal 1", CraftDir = "VAB", TimeoutSec = 600)]
        public static IEnumerator Flight_KerbalOnEvaIsABox(TestContext ctx)
        {
            yield return Wait.Seconds(3);
            Vessel v = FlightGlobals.ActiveVessel;
            Part pod = v.parts.FirstOrDefault(p => p.protoModuleCrew.Count > 0);
            Assert.NotNull(pod, "a crewed part on the Kerbal 1");
            ProtoCrewMember crew = pod.protoModuleCrew[0];

            ctx.Step(crew.name + " out on EVA");
            Assert.True(FlightEVA.fetch.spawnEVA(crew, pod, pod.airlock, true), "the kerbal went out on EVA");
            yield return Wait.Until(() => FlightGlobals.ActiveVessel != null && FlightGlobals.ActiveVessel.isEVA, 30, "the kerbal on EVA", shot: false);
            Vessel eva = FlightGlobals.ActiveVessel;
            Part kerbal = eva.rootPart;
            var aero = eva.GetComponent<FARVesselAero>();
            Assert.NotNull(aero, "FAR's vessel module on the kerbal");
            yield return Wait.Until(() => aero.isValid && aero.VehicleAero.Length > 0 && FARAPI.VesselVoxelizationCompletedEver(eva), 60,
                                    "FAR's model of the kerbal", shot: false);
            yield return Wait.Seconds(1);
            Assert.NotNull(kerbal.FindModuleImplementing<GeometryPartModule>(), "FAR's geometry module on the kerbal");
            Assert.NotNull(kerbal.FindModuleImplementing<FARAeroPartModule>(), "FAR's aero module on the kerbal");
            double volume = Far.Volume(aero.VehicleAero), length = aero.Length, area = aero.MaxCrossSectionArea;
            ctx.Record("kerbal", $"length {length:F3} m, max cross-section {area:F4} m2, volume {volume:F4} m3");
            Assert.Near(0.8, length, 0.12, "the box's height along its axis");
            Assert.Near(0.25, area, 0.25 * 0.25, "the box's 0.5 x 0.5 m cross-section");
            Assert.Near(0.2, volume, 0.25 * 0.2, "the box's volume");

            ctx.Step("Back aboard");
            KerbalEVA k = kerbal.FindModuleImplementing<KerbalEVA>();
            k?.BoardPart(pod);
            yield return Wait.Seconds(2);
        }

        /// <summary>
        /// The stock ComSat Lx (its launch clamp taken off: crafts/) carries its satellite in a procedural fairing: FAR shields the satellite's parts (no exposed
        /// area, no force) while the fairing is closed, and exposes them once it is jettisoned and the vessel revoxelized.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = "ComSat Lx Unclamped", CraftDir = "VAB", TimeoutSec = 900)]
        public static IEnumerator Flight_FairingShieldsItsPayloadUntilJettisoned(TestContext ctx)
        {
            yield return Wait.Seconds(3);
            yield return Wait.Until(() => FARAPI.VesselVoxelizationCompletedEver(FlightGlobals.ActiveVessel), 90, "the vessel voxelized", shot: false);
            yield return Wait.Seconds(2);   // FARVesselAero applies the new sections (shielding) in its next FixedUpdates
            Vessel v = FlightGlobals.ActiveVessel;
            Part fairingBase = v.parts.FirstOrDefault(p => p.Modules.Contains<ModuleProceduralFairing>());
            Assert.NotNull(fairingBase, "the ComSat Lx's fairing");
            List<Part> payload = Far.PayloadOf(v, fairingBase);
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
    }
}
