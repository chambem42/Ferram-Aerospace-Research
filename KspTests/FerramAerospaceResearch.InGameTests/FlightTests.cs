using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FerramAerospaceResearch.FARAeroComponents;
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

            ctx.Step("the rest of the flight numbers, for the vessel and the active vessel");
            // valid except while a revoxelization (FAR redoes the voxels in flight now and then) is under way; the
            // numbers below are all read in the frame it is
            yield return Wait.Until(() => FARAPI.VesselVoxelizationCompletedAndValid(v), 60, "a valid voxelization", shot: false);
            double rho = v.atmDensity, eas = FARAPI.VesselEAS(v), ias = FARAPI.VesselIAS(v);
            ctx.Record("airspeeds", $"TAS {v.srfSpeed:F1} EAS {eas:F1} IAS {ias:F1} m/s");
            Assert.Near(v.srfSpeed * Math.Sqrt(rho / 1.225), eas, 0.02 * eas, "EAS is TAS * sqrt(rho / rho0)");
            // the pitot reading at Mach 0.5, 3 km: compressibility puts it a little above the EAS
            Assert.True(ias > 0.99 * eas && ias < 1.06 * eas, $"IAS just above the EAS ({ias:F1} against {eas:F1} m/s)");
            Assert.Near(0, FARAPI.VesselSideslip(v), 1.0, "no sideslip heading along the velocity");
            Assert.True(FARAPI.VesselStallFrac(v) >= 0 && FARAPI.VesselStallFrac(v) < 0.05, $"unstalled at 5 deg ({FARAPI.VesselStallFrac(v):F3})");
            double bc = FARAPI.VesselBallisticCoeff(v), tv = FARAPI.VesselTermVelEst(v);
            ctx.Record("ballistic", $"coefficient {bc:F1} kg/m^2, terminal velocity {tv:F1} m/s");
            Assert.Near(v.GetTotalMass() * 1000 / (FARAPI.VesselDragCoeff(v) * FARAPI.VesselRefArea(v)), bc, 0.02 * bc,
                        "ballistic coefficient m / (Cd S)");
            double g = FlightGlobals.getGeeForceAtPosition(v.CoM).magnitude;
            Assert.Near(Math.Sqrt(2 * bc * g / rho), tv, 0.02 * tv, "terminal velocity sqrt(2 BC g / rho)");
            Vector3 aero = FARAPI.VesselAerodynamicForce(v);
            double appliedDrag = -Vector3.Dot(aero, v.srf_velocity.normalized);
            // the vessel flies free since the hold: against FAR's own coefficient now
            double cdqs = FARAPI.VesselDragCoeff(v) * FARAPI.VesselDynPres(v) * FARAPI.VesselRefArea(v);
            ctx.Record("drag", $"aerodynamic force {appliedDrag:F2} kN, Cd q S {cdqs:F2} kN (held {drag:F2})");
            Assert.Near(cdqs, appliedDrag, 0.02 * cdqs + 0.05, "the aerodynamic force's drag is Cd q S");
            Assert.False(FARAPI.VesselVoxelizationCompletedAndValid(null), "no voxelization for no vessel");
            Assert.Equal(FARAPI.VesselAoA(v), FARAPI.ActiveVesselAoA(), "the active vessel's angle of attack");
            Assert.Equal(FARAPI.VesselDynPres(v), FARAPI.ActiveVesselDynPres(), "the active vessel's dynamic pressure");
            Assert.Equal(FARAPI.VesselLiftCoeff(v), FARAPI.ActiveVesselLiftCoeff(), "the active vessel's Cl");
            Assert.Equal(FARAPI.VesselDragCoeff(v), FARAPI.ActiveVesselDragCoeff(), "the active vessel's Cd");
            Assert.Equal(FARAPI.VesselRefArea(v), FARAPI.ActiveVesselRefArea(), "the active vessel's reference area");
            Assert.Equal(bc, FARAPI.ActiveVesselBallisticCoeff(), "the active vessel's ballistic coefficient");
            Assert.Equal(tv, FARAPI.ActiveVesselTermVelEst(), "the active vessel's terminal velocity");
            Assert.Equal(FARAPI.VesselSideslip(v), FARAPI.ActiveVesselSideslip(), "the active vessel's sideslip");
            Assert.Equal(FARAPI.VesselStallFrac(v), FARAPI.ActiveVesselStallFrac(), "the active vessel's stall fraction");
            Assert.Equal(eas, FARAPI.ActiveVesselEAS(), "the active vessel's EAS");
            Assert.Equal(ias, FARAPI.ActiveVesselIAS(), "the active vessel's IAS");
            Assert.Equal(aero, FARAPI.ActiveVesselAerodynamicForce(), "the active vessel's aerodynamic force");
            Assert.Equal(FARAPI.VesselAerodynamicTorque(v), FARAPI.ActiveVesselAerodynamicTorque(), "the active vessel's aerodynamic torque");
            Assert.Equal(0.0, FARAPI.VesselTSFC(v), "no fuel burnt at zero throttle");

            ctx.Step("TSFC with the jets running");
            ModuleEngines[] jets = v.parts.SelectMany(p => p.FindModulesImplementing<ModuleEngines>())
                                    .Where(e => e.propellants.Any(pr => pr.name == "IntakeAir")).ToArray();
            Assert.True(jets.Length > 0, "the craft has jets");
            foreach (ModuleEngines e in jets)
                e.Activate();
            Ctrl.SetThrottle(1f);
            yield return Wait.Until(() => jets.Any(e => e.finalThrust > 1), 30, "the jets spooled up");
            yield return Wait.Seconds(1);
            ModuleEngines[] running = jets.Where(e => e.EngineIgnited && !e.engineShutdown).ToArray();
            double thrust = running.Sum(e => e.finalThrust), thrustIsp = running.Sum(e => e.finalThrust * e.realIsp);
            double tsfc = FARAPI.VesselTSFC(v);
            ctx.Record("tsfc", $"{tsfc:F4} /h, from {running.Length} jet(s): {thrust:F1} kN at Isp {thrustIsp / thrust:F0} s");
            // FAR's: per hour, 1 / the thrust-weighted Isp
            Assert.Near(3600 * thrust / thrustIsp, tsfc, 0.03 * tsfc, "TSFC is 3600 / Isp per hour");
            Assert.Equal(tsfc, FARAPI.ActiveVesselTSFC(), "the active vessel's TSFC");
            Ctrl.SetThrottle(0f);
        }

        /// <summary>
        /// FARAtmosphere's functions and a part's force modifier (FARAPI.SetPartAeroForceModifier) as another mod sets
        /// them: each reaches FAR's predictions and the force it applies, a throwing one falls back to the default, a
        /// non-thread-safe one keeps snapshots on the main thread, and clearing it puts the default back.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = Far.Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
        public static IEnumerator Flight_AtmosphereAndForceHooksReachFar(TestContext ctx)
        {
            yield return Wait.Seconds(5);
            FarFlightProbe.Condition c = Cruise(4);
            yield return Airborne(c);
            Vessel v = FlightGlobals.ActiveVessel;
            CelestialBody body = v.mainBody;
            Vector3 vel = v.srf_velocity;
            double alt = v.altitude;
            Vector3 upNormal = Vector3.ProjectOnPlane(v.upAxis, vel).normalized;

            (double Drag, double Lift, Vector3 Force) Predict()
            {
                FARAPI.CalculateVesselAeroForces(v, out Vector3 f, out _, vel, alt);
                return (-Vector3.Dot(f, vel.normalized), Vector3.Dot(f, upNormal), f);
            }

            ctx.Step("the default functions");
            Assert.False(FARAtmosphere.IsCustom, "the default atmosphere at the start");
            GasProperties gas = FARAtmosphere.GetGasProperties(v);
            Assert.Near(v.staticPressurekPa * 1000, gas.Pressure, 0.01 * gas.Pressure, "FAR's pressure is the vessel's");
            Assert.Near(PhysicsGlobals.IdealGasConstant / body.atmosphereMolarMass, FARAtmosphere.GetGasConstant(v), 1e-9, "the body's gas constant");
            Assert.Equal(body.atmosphereAdiabaticIndex, FARAtmosphere.GetAdiabaticIndex(v), "the body's adiabatic index");
            Assert.Near(Math.Sqrt(gas.AdiabaticIndex * gas.GasConstant * gas.Temperature), gas.SpeedOfSound, 1e-9, "the speed of sound");
            var p0 = Predict();
            ctx.Record("default", $"drag {p0.Drag:F2} lift {p0.Lift:F2} kN");
            Assert.True(p0.Drag > 0 && p0.Lift > 0, "drag and lift at 4 deg");

            try
            {
                ctx.Step("twice the pressure: twice the density, the Mach number the same");
                Func<CelestialBody, Vector3d, double, double> doubled = (b, lla, ut) => 2 * b.GetPressure(lla.z) * 1000;
                FARAtmosphere.SetPressureFunction(doubled);
                Assert.True(FARAtmosphere.IsCustom, "a custom atmosphere");
                Assert.Near(2 * gas.Pressure, FARAtmosphere.GetPressure(v), 0.01 * gas.Pressure, "the custom pressure");
                var p2 = Predict();
                ctx.Record("doubledPressure", $"drag {p2.Drag:F2} lift {p2.Lift:F2} kN");
                Assert.Near(2, p2.Lift / p0.Lift, 0.1, "lift doubles with the density");
                // the skin friction coefficient falls as the Reynolds number rises with the density
                Assert.True(p2.Drag / p0.Drag > 1.6 && p2.Drag / p0.Drag < 2.05, $"drag nearly doubles ({p2.Drag / p0.Drag:F3}x)");
                FARVesselSnapshot snap = FARAPI.CaptureVesselSnapshot(v);
                Assert.False(snap.OffMainThread, "a snapshot under a non-thread-safe function predicts on the main thread only");
                Assert.True(FARAPI.CalculateSnapshotAeroForcesAt(snap, out Vector3 fs, out _, vel, new Vector3d(v.latitude, v.longitude, alt),
                                                                Planetarium.GetUniversalTime()), "the snapshot predicts on the main thread");
                Assert.Near(0, (fs - p2.Force).magnitude, 0.01 * p2.Force.magnitude, "the snapshot's prediction under the custom pressure");
                FARAtmosphere.SetPressureFunctionThreadSafe(doubled);
                Assert.True(FARAPI.CaptureVesselSnapshot(v).OffMainThread, "one under a thread-safe function predicts on any thread");

                FARAtmosphere.SetPressureFunction(null);
                Assert.False(FARAtmosphere.IsCustom, "back to the default atmosphere");
                var back = Predict();
                Assert.Near(0, (back.Force - p0.Force).magnitude, 1e-3 * p0.Force.magnitude, "the default prediction again");

                ctx.Step("a hotter atmosphere, and a lighter gas, of the same density and speed of sound");
                FARAtmosphere.SetTemperatureFunction((b, lla, ut) => 1.21 * b.GetTemperature(lla.z));
                var hot = Predict();
                FARAtmosphere.SetTemperatureFunctionThreadSafe(null);
                FARAtmosphere.SetGasConstantFunction((b, lla, ut) => 1.21 * PhysicsGlobals.IdealGasConstant / b.atmosphereMolarMass);
                Assert.Near(1.21 * gas.GasConstant, FARAtmosphere.GetGasConstant(v), 1e-6 * gas.GasConstant, "the custom gas constant");
                var light = Predict();
                FARAtmosphere.SetGasConstantFunctionThreadSafe(null);
                ctx.Record("hotAndLight", $"hot lift {hot.Lift:F2} kN, light gas lift {light.Lift:F2} kN");
                // density / 1.21 and Mach / 1.1: lift down by about a fifth
                Assert.True(hot.Lift / p0.Lift > 0.72 && hot.Lift / p0.Lift < 0.88, $"lift in the hot air ({hot.Lift / p0.Lift:F3}x)");
                // the two differ in the viscosity only (it follows the temperature)
                Assert.Near(hot.Lift, light.Lift, 0.03 * hot.Lift, "the lighter gas lifts like the hotter air");

                ctx.Step("a stiffer gas: the same density, a faster speed of sound");
                FARAtmosphere.SetAdiabaticIndexFunction((b, lla, ut) => 1.21 * b.atmosphereAdiabaticIndex);
                Assert.Near(1.1 * gas.SpeedOfSound, FARAtmosphere.GetGasProperties(v).SpeedOfSound, 0.002 * gas.SpeedOfSound,
                            "the speed of sound with the custom adiabatic index");
                var stiff = Predict();
                FARAtmosphere.SetAdiabaticIndexFunctionThreadSafe(null);
                ctx.Record("stiff", $"lift {stiff.Lift:F2} kN");
                Assert.True(stiff.Lift < p0.Lift && stiff.Lift > 0.9 * p0.Lift, $"a little less lift at the lower Mach number ({stiff.Lift / p0.Lift:F3}x)");

                ctx.Step("a function that throws: FAR falls back to the default");
                FARAtmosphere.SetTemperatureFunction((b, lla, ut) => throw new InvalidOperationException("test"));
                var thrown = Predict();
                FARAtmosphere.SetTemperatureFunction(null);
                Assert.Near(0, (thrown.Force - p0.Force).magnitude, 1e-3 * p0.Force.magnitude, "the default prediction under a throwing function");

                ctx.Step("a part force modifier: the body forces scale by its factor");
                Part[] parts = v.parts.Where(p => p.FindModuleImplementing<FARAeroPartModule>() != null).ToArray();

                Vector3 Scaled(float k)
                {
                    foreach (Part p in parts)
                        FARAPI.SetPartAeroForceModifier(p, (part, local) => local * k);
                    return Predict().Force;
                }

                Vector3 f0 = Scaled(0), f1 = Scaled(1), f2 = Scaled(2);
                Assert.True(FARAPI.GetPartAeroForceModifier(parts[0]) != null, "the modifier is the part's");
                Assert.False(FARAPI.CaptureVesselSnapshot(v).OffMainThread, "a snapshot with a modifier predicts on the main thread only");
                foreach (Part p in parts)
                    FARAPI.SetPartAeroForceModifier(p, null);
                Vector3 none = Predict().Force;
                ctx.Record("modifier", $"x0 {f0.magnitude:F2}, x1 {f1.magnitude:F2}, x2 {f2.magnitude:F2}, none {none.magnitude:F2} kN; " +
                                       $"x1 - none {(f1 - none).magnitude:F4} kN");
                Assert.True(!float.IsNaN(f0.x + f1.x + f2.x), "finite forces under the modifier");
                // the wings carry most of the lift: the body parts a kN or so
                Assert.True((f1 - f0).magnitude > 0.3, $"the body parts carry some of the force ({(f1 - f0).magnitude:F2} kN)");
                Assert.Near(0, ((f2 - f1) - (f1 - f0)).magnitude, 0.02 * (f1 - f0).magnitude + 1e-3, "the body force scales with the factor");
                // to float rounding: a section with a zero component (no torque, say) keeps its force
                Assert.Near(0, (f1 - none).magnitude, 1e-5 * none.magnitude, "a factor of 1 is no modifier");
                Assert.Near(0, (none - p0.Force).magnitude, 1e-5 * none.magnitude, "the prediction with the modifiers gone is the first");

                ctx.Step("a 20 m/s headwind on the flying vessel");
                var calm = new List<FarFlightProbe.Sample>();
                yield return FarFlightProbe.Hold(c, 25, 10, calm, predict: false);
                int calls = 0;
                Vector3 windDir = -v.srf_velocity.normalized;
                FARAtmosphere.SetWindFunction((b, part, pos) =>
                {
                    calls++;
                    return 20 * windDir;
                });
                var windy = new List<FarFlightProbe.Sample>();
                yield return FarFlightProbe.Hold(c, 25, 10, windy, predict: false);
                FARAtmosphere.SetWindFunction(null);
                double calmDrag = calm.Average(s => s.Flight[0]), windyDrag = windy.Average(s => s.Flight[0]);
                double speed = calm.Average(s => s.Airspeed), expected = Math.Pow((speed + 20) / speed, 2);
                ctx.Record("headwind", $"drag {calmDrag:F2} -> {windyDrag:F2} kN ({windyDrag / calmDrag:F3}x, dynamic pressure {expected:F3}x), {calls} calls");
                Assert.True(calls > 0, "FAR asked the wind function");
                Assert.True(windyDrag / calmDrag > 1 + 0.5 * (expected - 1) && windyDrag / calmDrag < 1 + 1.5 * (expected - 1),
                            $"the drag rises with the airspeed into the wind ({windyDrag / calmDrag:F3}x against {expected:F3}x)");
            }
            finally
            {
                FARAtmosphere.SetWindFunction(null);
                FARAtmosphere.SetPressureFunction(null);
                FARAtmosphere.SetTemperatureFunction(null);
                FARAtmosphere.SetAdiabaticIndexFunction(null);
                FARAtmosphere.SetGasConstantFunction(null);
                foreach (Part p in v.parts)
                    if (p.FindModuleImplementing<FARAeroPartModule>() != null)
                        FARAPI.SetPartAeroForceModifier(p, null);
            }
        }
    }
}

