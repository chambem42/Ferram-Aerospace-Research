using System;
using System.Collections;
using System.Reflection;
using System.Text;
using FerramAerospaceResearch.FARAeroComponents;
using FerramAerospaceResearch.FARGUI.FAREditorGUI;
using FerramAerospaceResearch.FARGUI.FAREditorGUI.Simulation;
using FerramAerospaceResearch.FARGUI.FARFlightGUI;
using KSP.Localization;
using KspTest;
using UnityEngine;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>
    /// What the tests reach in FAR that it keeps private: the editor window's simulation and vehicle aerodynamics.
    /// Everything else the tests call directly (InternalsVisibleTo).
    /// </summary>
    internal static class Far
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

        private static T Field<T>(object o, string name) where T : class =>
            o?.GetType().GetField(name, Instance)?.GetValue(o) as T;

        public static EditorGUI Editor => EditorGUI.Instance ?? throw new UITestFailure("FAR's editor window is not there");

        /// <summary>The editor's instant-condition simulation (the static analysis and the stability derivatives' sim).</summary>
        public static InstantConditionSim EditorSim =>
            Field<InstantConditionSim>(Editor, "_instantSim") ?? throw new UITestFailure("FAR's editor has no _instantSim");

        /// <summary>The editor's vehicle aerodynamics: the voxel model of the craft and its cross-sections.</summary>
        public static VehicleAerodynamics EditorVehicle =>
            Field<VehicleAerodynamics>(Editor, "_vehicleAero") ?? throw new UITestFailure("FAR's editor has no _vehicleAero");

        public static bool EditorReady => EditorGUI.Instance != null && !EditorGUI.Instance.VoxelizationUpdateQueued &&
                                          Field<InstantConditionSim>(EditorGUI.Instance, "_instantSim")?.Ready == true;

        /// <summary>Waits for the craft in the editor to be voxelized and its simulation to have the result.</summary>
        public static IEnumerator WaitForEditorVoxels(double timeoutSec = 90)
        {
            yield return Wait.Until(() => EditorReady, timeoutSec, "FAR's editor voxels and sim", shot: false);
        }

        /// <summary>The instant sim at one condition, steady (no rates), no flaps or spoilers.</summary>
        public static InstantConditionSimOutput Instant(double alphaDeg, double mach, double betaDeg = 0, double pitch = 0)
        {
            var input = new InstantConditionSimInput(alphaDeg, betaDeg, 0, 0, 0, 0, mach, pitch, 0, false);
            EditorSim.GetClCdCmSteady(input, out InstantConditionSimOutput output, true);
            return output;
        }

        // ------------------------------------------------------------------ flight GUI

        /// <summary>The flight window's title starts with this ("FAR, " + the version).</summary>
        public const string MainWindow = "FAR, ";

        /// <summary>The flight data window's title (FARFlightDataTitle).</summary>
        public static string DataWindow => Localizer.Format("FARFlightDataTitle");

        /// <summary>
        /// The flight settings window: FlightGUI titles it with the tag FARFlightSettings, which the localization does not
        /// define (it has FARFlightSettingsTitle), so it shows the tag itself; either way the title has "Settings" in it.
        /// </summary>
        public const string SettingsWindow = "Settings";

        /// <summary>The settings window's groups, in FlightGUI's drop-down order.</summary>
        public const int GroupFlightData = 0, GroupStabilityAugmentation = 1, GroupAirspeed = 2, GroupAeroViz = 3;

        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

        private static object Get(object o, string name) =>
            (o?.GetType().GetField(name, Instance) ?? throw new UITestFailure($"{o?.GetType().Name} has no field {name}")).GetValue(o);

        private static void Set(object o, string name, object value) =>
            (o?.GetType().GetField(name, Instance) ?? throw new UITestFailure($"{o?.GetType().Name} has no field {name}")).SetValue(o, value);

        /// <summary>The active vessel's FAR flight GUI (a VesselModule).</summary>
        public static FlightGUI FlightGui(Vessel v = null) =>
            FARAPI.VesselFlightInfo(v ?? FlightGlobals.ActiveVessel) ?? throw new UITestFailure("the vessel has no FAR FlightGUI");

        /// <summary>FAR's flight window shown (FlightGUI.showGUI, what its toolbar button toggles).</summary>
        public static bool FlightGuiShown
        {
            get => (bool)typeof(FlightGUI).GetField("showGUI", Static).GetValue(null);
            set => typeof(FlightGUI).GetField("showGUI", Static).SetValue(null, value);
        }

        /// <summary>Whether one of the flight window's sub-windows is open: showFlightDataWindow, showSettingsWindow, ...</summary>
        public static bool SubWindowOpen(FlightGUI gui, string field) => (bool)Get(gui, field);

        public static void SetSubWindow(FlightGUI gui, string field, bool open) => Set(gui, field, open);

        /// <summary>
        /// Picks the settings window's group. Its drop-down list closes as soon as the mouse is not over it (there is
        /// no mouse in a test), so the selection is set the way the list's button would set it.
        /// </summary>
        public static void SetSettingsGroup(FlightGUI gui, int group) => Set(Get(gui, "settingsWindow"), "selectedOption", group);

        public static int SettingsGroup(FlightGUI gui) => (int)Get(Get(gui, "settingsWindow"), "selectedOption");

        /// <summary>The text of the flight data window's value column, as last drawn.</summary>
        public static string FlightDataText(FlightGUI gui) => ((StringBuilder)Get(Get(gui, "_flightDataGUI"), "dataStringBuilder")).ToString();

        /// <summary>The flight data window's section switches (FlightDataGUI.activeFlightDataSections), live.</summary>
        public static bool[] FlightDataSections(FlightGUI gui) => (bool[])Get(Get(gui, "_flightDataGUI"), "activeFlightDataSections");

        /// <summary>What the flight window's status box says (FlightStatusGUI.statusString).</summary>
        public static string FlightStatus(FlightGUI gui) => (string)Get(Get(gui, "_flightStatusGUI"), "statusString");

        // StabilityAugmentation.systemInstances[i]: 0 roll (wing leveler), 1 yaw, 2 pitch, 3 AoA limiter, 4 DPCR
        private static object StabSystem(FlightGUI gui, int i) => ((Array)Get(Get(gui, "_stabilityAugmentation"), "systemInstances")).GetValue(i);

        public static bool StabAugActive(FlightGUI gui, int system) => (bool)Get(StabSystem(gui, system), "active");

        public static void SetStabAug(FlightGUI gui, int system, bool on) => Set(StabSystem(gui, system), "active", on);

        /// <summary>The airspeed setting's mode and unit (enum values), to put back after a test.</summary>
        public static object[] AirspeedSetting(AirspeedSettingsGUI a) => new[] { Get(a, "velMode"), Get(a, "unitMode") };

        public static void SetAirspeedSetting(AirspeedSettingsGUI a, object[] saved)
        {
            Set(a, "velMode", saved[0]);
            Set(a, "unitMode", saved[1]);
        }

        /// <summary>Switches the aero visualization's tints (their setters are private: the GUI's toggles set them).</summary>
        public static void SetTints(AeroVisualizationGUI viz, bool cl, bool cd, bool stall)
        {
            typeof(AeroVisualizationGUI).GetProperty(nameof(AeroVisualizationGUI.TintForCl))?.SetValue(viz, cl, null);
            typeof(AeroVisualizationGUI).GetProperty(nameof(AeroVisualizationGUI.TintForCd))?.SetValue(viz, cd, null);
            typeof(AeroVisualizationGUI).GetProperty(nameof(AeroVisualizationGUI.TintForStall))?.SetValue(viz, stall, null);
        }

        /// <summary>The vessel's flight data logger (FlightGUI.flightDataLogger).</summary>
        public static FlightDataLogger Logger(FlightGUI gui) =>
            Field<FlightDataLogger>(gui, "flightDataLogger") ?? throw new UITestFailure("FAR's FlightGUI has no flightDataLogger");

        /// <summary>
        /// The tint FAR's aero visualization last gave a part (alpha 0 untinted): what the part's FARAeroPartModule last
        /// set on its MaterialColorUpdater (stock's, which writes it to the renderers' _TemperatureColor), else the most
        /// opaque _TemperatureColor on the part's renderers.
        /// </summary>
        public static Color PartTint(Part p)
        {
            FARAeroPartModule aero = p.Modules.GetModule<FARAeroPartModule>();
            MaterialColorUpdater updater = aero != null ? Field<MaterialColorUpdater>(aero, "materialColorUpdater") : null;
            // the colour it was last given (stock keeps it in a non-public field)
            if (updater?.GetType().GetField("setColor", Instance)?.GetValue(updater) is Color set)
                return set;
            Color best = new Color(0, 0, 0, 0);
            var block = new MaterialPropertyBlock();
            foreach (Renderer r in p.GetComponentsInChildren<Renderer>())
            {
                r.GetPropertyBlock(block);
                Color c = block.GetColor(PhysicsGlobals.TemperaturePropertyID);
                if (c.a > best.a) best = c;
            }

            return best;
        }
    }
}
