using System.Collections;
using System.Collections.Generic;
using KspTest;
using UnityEngine;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>FAR's prediction API at the edges: a repeated question, a question above the air, a snapshot gone stale.</summary>
    public static class PredictionTests
    {
        /// <summary>
        /// FARAPI's predictions on a stock plane in cruise: asked twice the same, the live vessel's prediction gives the
        /// same force and torque (from FAR's memo); asked at 100 km, above the atmosphere, nothing. A snapshot asked at
        /// 100 km still answers, with nothing; once the plane's model changes (gear down, voxelized again) the snapshot
        /// is no longer current and refuses to predict.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = Far.Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
        public static IEnumerator Flight_PredictionsInVacuumRepeatedAndStale(TestContext ctx)
        {
            yield return Wait.Seconds(5);
            FarFlightProbe.Condition c = Far.At(0.5, 3000, 4);
            yield return FlightTests.Airborne(c);
            yield return FarFlightProbe.Hold(c, 10, 1, new List<FarFlightProbe.Sample>(), predict: false);
            Vessel v = FlightGlobals.ActiveVessel;
            Vector3 vel = v.srf_velocity;
            double alt = v.altitude;
            try
            {
                ctx.Step("the same question twice");
                FARAPI.CalculateVesselAeroForces(v, out Vector3 f1, out Vector3 t1, vel, alt);
                FARAPI.CalculateVesselAeroForces(v, out Vector3 f2, out Vector3 t2, vel, alt);
                ctx.Record("cruise", $"force {f1}, torque {t1}");
                Assert.True(f1.magnitude > 1, $"a force in cruise ({f1.magnitude:F2} kN)");
                Assert.True(f1 == f2 && t1 == t2, $"the same answer to the same question ({f2} against {f1})");

                ctx.Step("above the atmosphere");
                FARAPI.CalculateVesselAeroForces(v, out Vector3 fHigh, out Vector3 tHigh, vel, 100000);
                Assert.True(fHigh == Vector3.zero && tHigh == Vector3.zero, $"no force at 100 km ({fHigh}, {tHigh})");
                FARVesselSnapshot snap = FARAPI.CaptureVesselSnapshot(v);
                Assert.NotNull(snap, "a snapshot of the plane");
                double ut = Planetarium.GetUniversalTime();
                Assert.True(FARAPI.CalculateSnapshotAeroForcesAt(snap, out Vector3 fs, out Vector3 ts, vel, new Vector3d(v.latitude, v.longitude, 100000), ut),
                            "the snapshot answers at 100 km");
                Assert.True(fs == Vector3.zero && ts == Vector3.zero, $"with no force ({fs}, {ts})");
                Assert.True(FARAPI.CalculateSnapshotAeroForcesAt(snap, out Vector3 fLow, out _, vel, new Vector3d(v.latitude, v.longitude, alt), ut),
                            "the snapshot answers where the plane is");
                Assert.True(fLow.magnitude > 1, $"with a force ({fLow.magnitude:F2} kN)");

                ctx.Step("the model changed");
                Ctrl.Gear(true);
                yield return Wait.Until(() => !snap.IsCurrent, 60, "the snapshot no longer current with the gear down", shot: false);
                Assert.False(FARAPI.CalculateSnapshotAeroForcesAt(snap, out Vector3 fStale, out _, vel, new Vector3d(v.latitude, v.longitude, alt), ut),
                             "a stale snapshot refuses to predict");
                Assert.True(fStale == Vector3.zero, "and gives no force");
            }
            finally
            {
                Ctrl.RetractGear();
            }
        }
    }
}
