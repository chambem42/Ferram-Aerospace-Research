using System.IO;
using KspTest.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace FerramAerospaceResearch.KspTests.Run
{
    /// <summary>
    ///     FAR's in-game tests, one KSP session each, on a new sandbox game with crafts/ in its SPH. Kind=Check tests assert
    ///     what they measure; Kind=Measure ones record numbers, CSVs and screenshots for comparing FAR builds and pass
    ///     unless the flight itself fails. The in-game tests' parameters are documented on them; the settings' params
    ///     (KSPTEST_PARAMS) go over them, e.g. crafts=My Plane for Far_ArCruise.
    /// </summary>
    public abstract class FarTestBase
    {
        private readonly ITestOutputHelper _output;

        protected FarTestBase(ITestOutputHelper output) => _output = output;

        /// <summary>The in-game test, KSP given its own timeout and a few minutes to load and quit.</summary>
        protected void Run(string test, int timeoutSec, string? parameters = null, string? save = null) =>
            Ksp.Run(_output, test, parameters, save, o => o.TimeoutSec = timeoutSec + 600);
    }

    /// <summary>FerramAerospaceResearch.KspTests: stock KSP and FAR.</summary>
    public class StockTests : FarTestBase
    {
        public StockTests(ITestOutputHelper output) : base(output) { }

        /// <summary>The stock planes' cruise and climb on AtmosphereAutopilot: lift, AoA, CL and the aero visualization.</summary>
        [KspFact(Requires = "AtmosphereAutopilot"), Trait("Kind", "Measure")]
        public void Far_ArCruise() => Run("Far_ArCruise", 10800);

        [KspFact, Trait("Kind", "Check")]
        public void Far_ComOffset_Editor() => Run("Far_ComOffset_Editor", 300);

        [KspFact, Trait("Kind", "Check")]
        public void Far_VoxelSize_Editor() => Run("Far_VoxelSize_Editor", 900);

        [KspFact, Trait("Kind", "Check")]
        public void Far_VoxelShot_Editor() => Run("Far_VoxelShot_Editor", 900);

        [KspFact, Trait("Kind", "Check")]
        public void Far_Regress_Editor() => Run("Far_Regress_Editor", 1800);

        [KspFact, Trait("Kind", "Check")]
        public void Far_PredictionAtmosphere_Flight() => Run("Far_PredictionAtmosphere_Flight", 600);

        [KspFact, Trait("Kind", "Check")]
        public void Far_PredictionIsolation() => Run("Far_PredictionIsolation", 600);

        [KspFact, Trait("Kind", "Check")]
        public void Far_PredictionThreads() => Run("Far_PredictionThreads", 900);

        [KspFact, Trait("Kind", "Check")]
        public void Far_FlightVsApi_NoiseFloor() => Run("Far_FlightVsApi_NoiseFloor", 1200);

        [KspFact(Requires = "AtmosphereAutopilot"), Trait("Kind", "Check")]
        public void Far_FlownVsPlaced() => Run("Far_FlownVsPlaced", 2400);

        [KspFact, Trait("Kind", "Check")]
        public void Far_CrossCondition() => Run("Far_CrossCondition", 3000);

        [KspFact, Trait("Kind", "Check")]
        public void Far_HeatmapHazards() => Run("Far_HeatmapHazards", 1800);

        [KspFact, Trait("Kind", "Check")]
        public void Far_WingBreakdown() => Run("Far_WingBreakdown", 600);

        [KspFact, Trait("Kind", "Check")]
        public void Far_BodyBreakdown() => Run("Far_BodyBreakdown", 600);

        [KspFact, Trait("Kind", "Check")]
        public void Far_ControlCamber_Flight() => Run("Far_ControlCamber_Flight", 3600);

        [KspFact(Requires = "AtmosphereAutopilot"), Trait("Kind", "Check")]
        public void Far_ControlCamber_Cruise() => Run("Far_ControlCamber_Cruise", 5400);

        [KspFact(Requires = "AtmosphereAutopilot"), Trait("Kind", "Check")]
        public void Far_FlightProfile() => Run("Far_FlightProfile", 3600);

        [KspFact, Trait("Kind", "Measure")]
        public void Far_FlightParity() => Run("Far_FlightParity", 3000);

        [KspFact, Trait("Kind", "Measure")]
        public void Far_ControlCamber_AB() => Run("Far_ControlCamber_AB", 3600);

        [KspFact, Trait("Kind", "Measure")]
        public void Far_WingInteractions() => Run("Far_WingInteractions", 1800);

        [KspFact, Trait("Kind", "Measure")]
        public void Far_Planform() => Run("Far_Planform", 1800);

        [KspFact, Trait("Kind", "Measure")]
        public void Far_CoupleBalance() => Run("Far_CoupleBalance", 3600);

        [KspFact, Trait("Kind", "Measure")]
        public void Far_VoxelRepeat() => Run("Far_VoxelRepeat", 1800);

        [KspFact, Trait("Kind", "Measure")]
        public void Far_TopShots() => Run("Far_TopShots", 1800);
    }

    /// <summary>FerramAerospaceResearch.KspTests.MechJeb: flights on MechJeb.</summary>
    public class MechJebTests : FarTestBase
    {
        private const string MechJeb = "MechJeb2/Plugins/MechJeb2.dll";

        public MechJebTests(ITestOutputHelper output) : base(output) { }

        [KspFact(Requires = MechJeb), Trait("Kind", "Check")]
        public void Far_ComOffset_Flight() => Run("Far_ComOffset_Flight", 900);

        [KspFact(Requires = MechJeb), Trait("Kind", "Check")]
        public void Far_VoxelSize_Flight() => Run("Far_VoxelSize_Flight", 1800);

        [KspFact(Requires = MechJeb), Trait("Kind", "Check")]
        public void Far_Heating_Flight() => Run("Far_Heating_Flight", 1800);

        [KspFact(Requires = MechJeb), Trait("Kind", "Check")]
        public void Far_Regress_Flight() => Run("Far_Regress_Flight", 2400);
    }

    /// <summary>FerramAerospaceResearch.KspTests.RP1: reentries in an RP-1 career, from the save template rp1-career.</summary>
    public class Rp1Tests : FarTestBase
    {
        private const string Rp1 = "MechJeb2/Plugins/MechJeb2.dll;RP-1/Plugins/RP0.dll";
        private const string Career = "rp1-career";

        public Rp1Tests(ITestOutputHelper output) : base(output) { }

        private void RunInCareer(string test, int timeoutSec)
        {
            if (!Directory.Exists(Path.Combine(KspSettings.Current.Saves, Career)))
                throw new SkipException($"needs an RP-1 career save template '{Career}' in {KspSettings.Current.Saves} (KSPTEST_SAVES)");
            Run(test, timeoutSec, save: Career);
        }

        [KspFact(Requires = Rp1), Trait("Kind", "Check")]
        public void Reentry_Predict() => RunInCareer("Reentry_Predict", 600);

        [KspFact(Requires = Rp1), Trait("Kind", "Check")]
        public void Reentry_Replay() => RunInCareer("Reentry_Replay", 600);

        [KspFact(Requires = Rp1), Trait("Kind", "Check")]
        public void Reentry_Analyse() => RunInCareer("Reentry_Analyse", 900);

        [KspFact(Requires = Rp1), Trait("Kind", "Check")]
        public void Reentry_Borderline() => RunInCareer("Reentry_Borderline", 7200);

        [KspFact(Requires = Rp1), Trait("Kind", "Measure")]
        public void Reentry_Fly() => RunInCareer("Reentry_Fly", 3000);
    }
}
