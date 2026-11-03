using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ferram4;
using FerramAerospaceResearch.FARAeroComponents;
using FerramAerospaceResearch.FARPartGeometry;
using FerramAerospaceResearch.Settings;
using KspTest;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>What FAR has set up once the game has loaded: its part modules, its stock wing conversions, its atmospheres.</summary>
    public static class LoadTests
    {
        /// <summary>
        /// Every part but a launch clamp gets FAR's geometry and aero modules (_FARPartModule.cfg), and no part keeps a
        /// stock lifting surface beside a FAR wing (its lift would count twice).
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.MainMenu, TimeoutSec = 600)]
        public static IEnumerator Load_EveryPartHasFarModules(TestContext ctx)
        {
            var missing = new List<string>();
            var doubled = new List<string>();
            int parts = 0, wings = 0;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                Part p = ap.partPrefab;
                // launch clamps are left out by FAR's patch; EVA kerbals are made by the game, not from a part config
                if (p == null || p.Modules.Contains<LaunchClamp>() || ap.name.StartsWith("kerbalEVA"))
                    continue;
                parts++;
                if (!p.Modules.Contains<GeometryPartModule>() || !p.Modules.Contains<FARAeroPartModule>())
                    missing.Add(ap.name);
                if (p.Modules.Contains<FARWingAerodynamicModel>())
                {
                    wings++;
                    if (p.Modules.Contains<ModuleLiftingSurface>())
                        doubled.Add(ap.name);
                }
            }

            ctx.Record("parts", parts);
            ctx.Record("farWings", wings);
            Assert.True(parts > 100, $"the stock parts loaded ({parts})");
            Assert.True(missing.Count == 0, $"every part has GeometryPartModule and FARAeroPartModule; without: {string.Join(", ", missing.Take(20))}");
            Assert.True(doubled.Count == 0, $"no part has a stock lifting surface beside a FAR wing: {string.Join(", ", doubled.Take(20))}");
            yield break;
        }

        /// <summary>FAR's conversions of stock wings and control surfaces (FerramAerospaceResearch.cfg).</summary>
        [UITest(Kind = "Check", StartsAt = StartState.MainMenu, TimeoutSec = 600)]
        public static IEnumerator Load_StockWingsAreFarWings(TestContext ctx)
        {
            // part name -> a control surface (true) or a fixed wing (false)
            var expected = new Dictionary<string, bool>
            {
                ["wingConnector"] = false, ["deltaWing"] = false, ["sweptWing1"] = false, ["wingStrake"] = false,
                ["StandardCtrlSrf"] = true, ["elevon2"] = true,
            };
            foreach (KeyValuePair<string, bool> e in expected)
            {
                AvailablePart ap = PartLoader.getPartInfoByName(e.Key);
                Assert.NotNull(ap, "stock part " + e.Key);
                FARWingAerodynamicModel wing = ap.partPrefab.Modules.GetModule<FARWingAerodynamicModel>();
                Assert.NotNull(wing, e.Key + "'s FAR wing module");
                Assert.Equal(e.Value, wing is FARControllableSurface, e.Key + " is a control surface");
                Assert.True(wing.b_2 > 0 && wing.MAC > 0, $"{e.Key} has a span and chord (b_2 {wing.b_2}, MAC {wing.MAC})");
                Assert.False(ap.partPrefab.Modules.Contains<ModuleLiftingSurface>(), e.Key + " has no stock lifting surface left");
                ctx.Record(e.Key, $"b_2 {wing.b_2}, MAC {wing.MAC}, taper {wing.TaperRatio}, sweep {wing.MidChordSweep}");
            }

            yield break;
        }

        /// <summary>
        /// FARAeroStress.cfg's templates are loaded, and each stock part gets the one its crew and resources call for:
        /// uncrewed and empty, structural; only solid fuel, SRB; only liquid fuel, aircraft tank; liquid fuel and oxidizer,
        /// rocket tank; crewed, crewed; with an ablator, the first (structural) whatever else it has.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.MainMenu, TimeoutSec = 600)]
        public static IEnumerator Load_StressTemplatesForStockParts(TestContext ctx)
        {
            string[] names = FARAeroStress.StressTemplates.Select(t => t.Name).ToArray();
            ctx.Record("templates", string.Join(", ", names));
            foreach (string n in new[] { "structural", "crewed", "rocketFuelTank", "aircraftFuelTank", "SRB", "wingStress", "ctrlSurfStress" })
                Assert.True(names.Contains(n), "stress template " + n);
            FARPartStressTemplate rocket = FARAeroStress.StressTemplates.First(t => t.Name == "rocketFuelTank");
            Assert.Near(30, rocket.XZMaxStress, 1e-9, "rocketFuelTank's XZmaxStress (FARAeroStress.cfg)");
            Assert.True(rocket.FlowModeNeeded, "rocketFuelTank counts only stack-priority resources");

            var wrong = new List<string>();
            int checkedParts = 0;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                Part p = ap.partPrefab;
                if (p == null || ap.name.StartsWith("kerbalEVA") || p.Modules.Contains<LaunchClamp>())
                    continue;
                string[] res = p.Resources.Cast<PartResource>().Select(r => r.resourceName).ToArray();
                string expected = null;
                if (p.HasModuleImplementing<ModuleAblator>() || res.Contains("Ablator")) expected = "structural";
                else if (p.CrewCapacity > 0) expected = "crewed";
                else if (res.Length == 0) expected = "structural";
                else if (res.All(r => r == "SolidFuel")) expected = "SRB";
                else if (res.All(r => r == "LiquidFuel")) expected = "aircraftFuelTank";
                else if (res.Length == 2 && res.Contains("LiquidFuel") && res.Contains("Oxidizer")) expected = "rocketFuelTank";
                if (expected == null) continue;
                checkedParts++;
                string got = FARAeroStress.DetermineStressTemplate(p).Name;
                if (got != expected)
                    wrong.Add($"{ap.name} ({string.Join("+", res)}{(p.CrewCapacity > 0 ? ", crewed" : "")}): {got}, not {expected}");
            }

            ctx.Record("partsChecked", checkedParts);
            Assert.True(checkedParts > 100, $"stock parts checked ({checkedParts})");
            Assert.True(wrong.Count == 0, $"{wrong.Count} parts with the wrong stress template: {string.Join("; ", wrong.Take(15))}");
            yield break;
        }

        /// <summary>Every body with an atmosphere has FAR's settings for it (FARAeroData.cfg, or the defaults).</summary>
        [UITest(Kind = "Check", StartsAt = StartState.MainMenu, TimeoutSec = 600)]
        public static IEnumerator Load_AtmosphereSettingsForEveryBody(TestContext ctx)
        {
            int withAtmosphere = 0;
            foreach (CelestialBody body in FlightGlobals.Bodies.Where(b => b.atmosphere))
            {
                withAtmosphere++;
                Assert.True(FARAeroData.AtmosphericConfiguration.TryGetValue(body.flightGlobalsIndex, out BodySettings s),
                            body.bodyName + "'s FAR settings");
                Assert.True(s.ReferenceViscosity > 0 && s.ReferenceTemperature > 0,
                            $"{body.bodyName}: viscosity {s.ReferenceViscosity} at {s.ReferenceTemperature} K");
                ctx.Record(body.bodyName, $"viscosity {s.ReferenceViscosity:G4} at {s.ReferenceTemperature} K");
            }

            Assert.True(withAtmosphere >= 5, $"the stock bodies with an atmosphere ({withAtmosphere}: Kerbin, Eve, Duna, Jool, Laythe)");
            CelestialBody home = FlightGlobals.GetHomeBody();
            Assert.Near(home.atmosphereAdiabaticIndex,
                        FARAtmosphere.GetAdiabaticIndex(home, new Vector3d(0, 0, 0), Planetarium.GetUniversalTime()), 1e-9,
                        "FAR's adiabatic index at Kerbin's surface is the body's");
            yield break;
        }
    }
}
