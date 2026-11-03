using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using KspTest;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>
    /// FAR in flight on a stock plane placed in level flight: the force it applies against the vessel's own acceleration,
    /// its prediction API against the force it applies, and FARAPI's flight numbers against the conditions.
    /// </summary>
    public static class FlightTests
    {
        private const string Plane = "Aeris 4A";

        // over the sea east of KSC, heading east
        private static FarFlightProbe.Condition Cruise(double aoaDeg) =>
            new FarFlightProbe.Condition { Name = $"cruise {aoaDeg}", Mach = 0.5, AltM = 3000, AoADeg = aoaDeg, Lat = 0, Lon = -60, HeadingDeg = 90 };

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
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
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
        [UITest(Kind = "Check", Explicit = true, StartsAt = StartState.Flight, Craft = Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
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

        /// <summary>FARAPI's flight numbers: dynamic pressure from the air and speed, the angle of attack held, Cl and Cd signs.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
        public static IEnumerator Flight_ApiReportsTheConditions(TestContext ctx)
        {
            yield return Wait.Seconds(5);
            Flight.IgnoreMaxTemperature(true);
            Ctrl.SetThrottle(0f);
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
        }
    }
}
