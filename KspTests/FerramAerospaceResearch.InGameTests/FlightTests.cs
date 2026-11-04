using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FerramAerospaceResearch.FARGUI.FARFlightGUI;
using KspTest;
using UnityEngine;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>
    /// FAR in flight on a stock plane placed in level flight: the force it applies against the vessel's own acceleration,
    /// its prediction API against the force it applies, and FARAPI's flight numbers against the conditions.
    /// </summary>
    public static class FlightTests
    {

        // over the sea east of KSC, heading east
        private static FarFlightProbe.Condition Cruise(double aoaDeg) => Far.At(0.5, 3000, aoaDeg, $"cruise {aoaDeg}");

        /// <summary>
        /// The craft in the air at the condition, SAS off (it would move the control surfaces) and gear up: placed once
        /// to come off the runway and settle (FAR revoxelizes and rebuilds its prediction state once the vessel is
        /// flying), then placed again at the condition.
        /// </summary>
        internal static IEnumerator Airborne(FarFlightProbe.Condition c)
        {
            Vessel v = FlightGlobals.ActiveVessel;
            v.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            Flight.IgnoreMaxTemperature(true);
            Ctrl.SetThrottle(0f);
            yield return FarFlightProbe.Place(c);
            Ctrl.RetractGear();
            yield return Wait.Seconds(8);
            yield return FarFlightProbe.Place(c);
        }

        /// <summary>
        /// Held at an angle of attack: the drag and lift FAR applies are what accelerate the vessel (its kinematic force:
        /// at zero throttle with the gear up, the aero force is the only one but gravity).
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = Far.Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
        public static IEnumerator Flight_AppliedForceIsTheAeroForce(TestContext ctx)
        {
            yield return Wait.Seconds(5);   // FAR voxelizes the craft after it loads
            FarFlightProbe.Condition c = Cruise(4);
            yield return Airborne(c);
            var samples = new List<FarFlightProbe.Sample>();
            yield return FarFlightProbe.Hold(c, 25, 50, samples, predict: false);
            Assert.True(samples.Count >= 40, $"samples held ({samples.Count})");
            double drag = samples.Average(s => s.Flight[0]), lift = samples.Average(s => s.Flight[1]);
            double kinDrag = samples.Where(s => !double.IsNaN(s.Kinematic[0])).Average(s => s.Kinematic[0]);
            double kinLift = samples.Where(s => !double.IsNaN(s.Kinematic[1])).Average(s => s.Kinematic[1]);
            ctx.Record("drag", $"applied {drag:F2} kN, kinematic {kinDrag:F2}");
            ctx.Record("lift", $"applied {lift:F2} kN, kinematic {kinLift:F2}");
            Assert.True(drag > 0 && lift > 0, $"drag ({drag:F2} kN) and lift ({lift:F2} kN) at 4 deg");
            Assert.Near(drag, kinDrag, 0.05 * drag + 0.2, "drag applied against the drag the vessel feels");
            Assert.Near(lift, kinLift, 0.05 * lift + 0.2, "lift applied against the lift the vessel feels");
        }

        /// <summary>
        /// FAR's prediction API (FARAPI.CalculateVesselAeroForces) at the state the vessel flies in gives the force FAR
        /// applies there. Explicit (asked for by name only) while master's API takes only an altitude: there it predicts
        /// the Aeris 4A at Mach 0.5, 3 km, 4 deg with 40% too little drag and 8% too much lift (8.5 against 14.1 kN,
        /// 137.9 against 127.3 kN); the place-and-time API of the prediction-accuracy work
        /// (CalculateVesselAeroForcesAt) agrees to 0.04%.
        /// </summary>
        [UITest(Kind = "Check", Explicit = true, StartsAt = StartState.Flight, Craft = Far.Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
        public static IEnumerator Flight_PredictionIsTheAppliedForce(TestContext ctx)
        {
            yield return Wait.Seconds(5);
            FarFlightProbe.Condition c = Cruise(4);
            yield return Airborne(c);
            ctx.Record("predictionApi", FarApi.HasPredictAt ? "CalculateVesselAeroForcesAt" : "CalculateVesselAeroForces (altitude only)");
            var samples = new List<FarFlightProbe.Sample>();
            yield return FarFlightProbe.Hold(c, 25, 50, samples);
            double drag = samples.Average(s => s.Flight[0]), lift = samples.Average(s => s.Flight[1]);
            double predDrag = samples.Average(s => s.Predicted[0]), predLift = samples.Average(s => s.Predicted[1]);
            ctx.Record("drag", $"applied {drag:F2} kN, predicted {predDrag:F2}");
            ctx.Record("lift", $"applied {lift:F2} kN, predicted {predLift:F2}");
            Assert.Near(drag, predDrag, 0.03 * drag + 0.05, "FAR's prediction of the drag it applies");
            Assert.Near(lift, predLift, 0.03 * lift + 0.05, "FAR's prediction of the lift it applies");
        }

        /// <summary>
        /// FAR's vessel snapshot (FARAPI.CaptureVesselSnapshot, CalculateSnapshotAeroForcesAt: a prediction at a place and
        /// time, from a copy of the vessel's aero model) at the state the vessel flies in: the same answer on a worker
        /// thread as on the main thread, and full pitch input changes the pitching moment.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = Far.Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
        public static IEnumerator Flight_SnapshotIsThreadSafeAndTakesControls(TestContext ctx) => Snapshot(ctx, againstApplied: false);

        /// <summary>
        /// The snapshot's prediction at the state the vessel flies in is the force FAR applies there. Explicit: on master the
        /// snapshot path predicts like the altitude-only API, the Aeris 4A at Mach 0.5, 3 km, 4 deg with 8.6 kN of drag against
        /// 14.2 kN applied and 138.0 kN of total force against 128.1 (measured).
        /// </summary>
        [UITest(Kind = "Check", Explicit = true, StartsAt = StartState.Flight, Craft = Far.Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
        public static IEnumerator Flight_SnapshotPredictsTheAppliedForce(TestContext ctx) => Snapshot(ctx, againstApplied: true);

        private static IEnumerator Snapshot(TestContext ctx, bool againstApplied)
        {
            yield return Wait.Seconds(5);
            FarFlightProbe.Condition c = Cruise(4);
            yield return Airborne(c);
            var samples = new List<FarFlightProbe.Sample>();
            yield return FarFlightProbe.Hold(c, 25, 30, samples, predict: false);
            double drag = samples.Average(s => s.Flight[0]), lift = samples.Average(s => s.Flight[1]);

            Vessel v = FlightGlobals.ActiveVessel;
            FARVesselSnapshot snap = FARAPI.CaptureVesselSnapshot(v);
            Assert.NotNull(snap, "a snapshot of the flying vessel");
            Vector3 vel = v.srf_velocity;
            var where = new Vector3d(v.latitude, v.longitude, v.altitude);
            double ut = Planetarium.GetUniversalTime();
            Assert.True(FARAPI.CalculateSnapshotAeroForcesAt(snap, out Vector3 f, out Vector3 tq, vel, where, ut), "the snapshot prediction ran");
            double predDrag = -Vector3.Dot(f, vel.normalized);
            double predTotal = f.magnitude, total = Math.Sqrt(drag * drag + lift * lift);
            ctx.Record("drag", $"applied {drag:F2} kN, snapshot {predDrag:F2}");
            ctx.Record("force", $"applied |F| {total:F2} kN, snapshot {predTotal:F2}");
            if (againstApplied)
            {
                Assert.Near(drag, predDrag, 0.03 * drag + 0.05, "the snapshot's drag against the drag FAR applies");
                Assert.Near(total, predTotal, 0.03 * total + 0.05, "the snapshot's total force against the one FAR applies");
            }

            // the snapshot reads no live game state: a worker thread gets the same answer
            var worker = System.Threading.Tasks.Task.Run(() =>
            {
                bool ok = FARAPI.CalculateSnapshotAeroForcesAt(snap, out Vector3 wf, out Vector3 wt, vel, where, ut);
                return (ok, wf, wt);
            });
            (bool wok, Vector3 wf2, Vector3 wt2) = worker.Result;
            ctx.Record("worker", $"{wf2} against {f}");
            Assert.True(wok, "the snapshot prediction ran on a worker thread");
            Assert.Near(0, (wf2 - f).magnitude, 1e-4 * f.magnitude + 1e-6, "the worker thread's force against the main thread's");
            Assert.Near(0, (wt2 - tq).magnitude, 1e-4 * tq.magnitude + 1e-6, "the worker thread's torque against the main thread's");

            // full nose-up and nose-down input: the control surfaces move, and so does the pitching moment
            Vector3 pitchAxis = Flight.PitchUpAxis(v);
            Assert.True(FARAPI.CalculateSnapshotAeroForcesWithControlsAt(snap, out _, out Vector3 tUp, vel, where, ut, 1, 0, 0), "prediction with pitch +1");
            Assert.True(FARAPI.CalculateSnapshotAeroForcesWithControlsAt(snap, out _, out Vector3 tDown, vel, where, ut, -1, 0, 0), "prediction with pitch -1");
            double mUp = Vector3.Dot(tUp, pitchAxis), mDown = Vector3.Dot(tDown, pitchAxis), m0 = Vector3.Dot(tq, pitchAxis);
            ctx.Record("pitchMoment", $"input -1 {mDown:F2}, 0 {m0:F2}, +1 {mUp:F2} kN m");
            Assert.True(mUp > m0 && m0 > mDown, $"pitching moment rises with pitch input ({mDown:F2} < {m0:F2} < {mUp:F2} kN m)");
        }

        /// <summary>FARAPI's flight numbers: dynamic pressure from the air and speed, the angle of attack held, Cl and Cd signs.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = Far.Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
        public static IEnumerator Flight_ApiReportsTheConditions(TestContext ctx)
        {
            yield return Wait.Seconds(5);
            FarFlightProbe.Condition c = Cruise(5);
            yield return Airborne(c);
            var samples = new List<FarFlightProbe.Sample>();
            yield return FarFlightProbe.Hold(c, 25, 10, samples, predict: false);

            Vessel v = FlightGlobals.ActiveVessel;
            double q = 0.5 * v.atmDensity * v.srfSpeed * v.srfSpeed / 1000;   // kPa, as FAR reports it
            ctx.Record("dynPres", $"FAR {FARAPI.VesselDynPres(v):F3} kPa, 1/2 rho v^2 {q:F3}");
            ctx.Record("aoa", FARAPI.VesselAoA(v));
            ctx.Record("coefficients", $"Cl {FARAPI.VesselLiftCoeff(v):F4} Cd {FARAPI.VesselDragCoeff(v):F4} refArea {FARAPI.VesselRefArea(v):F2}");
            Assert.Near(q, FARAPI.VesselDynPres(v), 0.02 * q, "dynamic pressure is 1/2 rho v^2");
            Assert.Near(c.AoADeg, FARAPI.VesselAoA(v), 1.0, "the angle of attack held");
            Assert.True(FARAPI.VesselLiftCoeff(v) > 0, "positive Cl at a positive angle of attack");
            Assert.True(FARAPI.VesselDragCoeff(v) > 0, "positive Cd");
            Assert.True(FARAPI.VesselRefArea(v) > 0, "a reference area");
            Assert.True(FARAPI.VesselVoxelizationCompletedEver(v), "the vessel was voxelized");

            // the altitude-only prediction: drag against the velocity and lift up, of the flight's size (how close
            // it comes is Flight_PredictionIsTheAppliedForce's, explicit while master is 40% short on drag)
            ctx.Step("CalculateVesselAeroForces at the vessel's speed and altitude");
            double drag = samples.Average(s => s.Flight[0]), lift = samples.Average(s => s.Flight[1]);
            Vector3 vel = v.srf_velocity;
            FARAPI.CalculateVesselAeroForces(v, out Vector3 f, out Vector3 tq, vel, v.altitude);
            Vector3 up = Vector3.ProjectOnPlane(v.upAxis, vel).normalized;
            double predDrag = -Vector3.Dot(f, vel.normalized), predLift = Vector3.Dot(f, up);
            ctx.Record("altitudeApi", $"drag {predDrag:F2} kN (flying {drag:F2}), lift {predLift:F2} kN (flying {lift:F2}), torque {tq.magnitude:F2} kN m");
            Assert.True(predDrag > 0.3 * drag && predDrag < 3 * drag, $"the predicted drag is the flight's to within a factor of 3 ({predDrag:F2} against {drag:F2} kN)");
            Assert.True(predLift > 0.5 * lift && predLift < 2 * lift, $"the predicted lift is the flight's to within a factor of 2 ({predLift:F2} against {lift:F2} kN)");

            ctx.Step("ToggleAirspeedDisplay, for every vessel and for this one");
            AirspeedSettingsGUI air = Far.FlightGui(v).airSpeedGUI;
            bool allAtStart = AirspeedSettingsGUI.allEnabled, vesselAtStart = air.enabled;
            try
            {
                Assert.True(FARAPI.ToggleAirspeedDisplay(false), "turning the airspeed display off everywhere");
                Assert.False(AirspeedSettingsGUI.allEnabled, "the airspeed display off everywhere");
                Assert.True(FARAPI.ToggleAirspeedDisplay(), "toggling it everywhere");
                Assert.True(AirspeedSettingsGUI.allEnabled, "toggled back on everywhere");
                Assert.True(FARAPI.ToggleAirspeedDisplay(false, v), "turning it off for the vessel");
                Assert.False(air.enabled, "off for the vessel");
                Assert.True(AirspeedSettingsGUI.allEnabled, "still on everywhere else");
                Assert.True(FARAPI.ToggleAirspeedDisplay(null, v), "toggling it for the vessel");
                Assert.True(air.enabled, "back on for the vessel");
            }
            finally
            {
                AirspeedSettingsGUI.allEnabled = allAtStart;
                air.enabled = vesselAtStart;
            }
        }
    }
}
