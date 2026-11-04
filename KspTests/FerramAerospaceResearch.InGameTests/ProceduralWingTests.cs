using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ferram4;
using FerramAerospaceResearch.FARGUI.FAREditorGUI.Simulation;
using KspTest;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>
    /// B9 procedural wings in FAR, against the stock wings they stand in for. The "Aeris 4A PW" (crafts/) is the stock
    /// Aeris 4A with its four main wing panels (two structural wings, two delta wings) made B9 procedural panels of the
    /// planform FAR gives the stock parts, in the same places (made by crafts/aeris_pw.py in the run project). B9
    /// hands FAR its panels' geometry (semispan, mean chord, taper, mid-chord sweep) and has FAR rebuild the wing; so
    /// FAR sees the twin's panels as the stock ones, and the twin flies as the stock plane.
    /// </summary>
    public static class ProceduralWingTests
    {
        private const string Twin = "Aeris 4A PW";
        private const string B9Wing = "B9.Aero.Wing.Procedural.TypeA";

        private sealed class Panel
        {
            public uint Id;
            public string Part;
            public double S, Semispan, Mac, Taper, Sweep;

            public override string ToString() =>
                string.Format(CultureInfo.InvariantCulture, "{0}: S {1:F3} m2, b/2 {2:F3} m, MAC {3:F3} m, taper {4:F3}, sweep {5:F2} deg",
                              Part, S, Semispan, Mac, Taper, Sweep);
        }

        /// <summary>The craft's main wing panels as FAR sees them, by the part's craft id (the twin keeps the stock ids).</summary>
        private static Dictionary<uint, Panel> Panels(IEnumerable<Part> parts, Func<Part, bool> which) =>
            parts.Where(which).ToDictionary(p => p.craftID, p =>
            {
                FARWingAerodynamicModel w = p.Modules.GetModule<FARWingAerodynamicModel>() ??
                                            throw new UITestFailure($"{p.partInfo.name} has no FAR wing model");
                return new Panel { Id = p.craftID, Part = p.partInfo.name, S = w.S, Semispan = w.b_2_actual, Mac = w.MAC_actual, Taper = w.TaperRatio, Sweep = w.MidChordSweep };
            });

        private sealed class Aero
        {
            public double[] Cl = new double[3], Cd = new double[3];
        }

        private static readonly double[] Alphas = { 0, 2, 4 };

        private static Aero Measure()
        {
            var a = new Aero();
            for (int i = 0; i < Alphas.Length; i++)
            {
                // a few calls: the wings' stall state settles
                InstantConditionSimOutput o = default;
                for (int k = 0; k < 4; k++)
                    o = Far.Instant(Alphas[i], 0.5);
                a.Cl[i] = o.Cl;
                a.Cd[i] = o.Cd;
            }

            return a;
        }

        /// <summary>
        /// In the SPH, the twin then the stock Aeris 4A. Each B9 panel's FAR wing is the stock part's in its place: the
        /// same area, semispan, mean chord, taper and mid-chord sweep, as B9 computed them. And the whole plane: at
        /// Mach 0.5 and 0, 2 and 4 deg, the twin's lift is the stock plane's, its lift slope too, and its drag near it
        /// (the panels' thickness and edges are B9's own, so the voxel model differs a little).
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = Twin, CraftDir = "SPH", TimeoutSec = 600)]
        public static IEnumerator Procedural_B9TwinIsTheStockAeris(TestContext ctx)
        {
            ctx.Step("The twin: its B9 panels as FAR and B9 see them");
            yield return Far.WaitForEditorVoxels();
            List<Part> twinParts = EditorLogic.fetch.ship.parts;
            Dictionary<uint, Panel> b9 = Panels(twinParts, p => p.partInfo.name == B9Wing);
            Assert.Equal(4, b9.Count, "the twin's four B9 panels");
            foreach (Part p in twinParts.Where(p => p.partInfo.name == B9Wing))
            {
                PartModule wp = p.Modules["WingProcedural"];
                Panel f = b9[p.craftID];
                ctx.Record("b9." + p.craftID, f + $"; B9 S {Refl.Get(wp, "aeroStatSurfaceArea")}");
                Assert.Near((double)Refl.Get(wp, "aeroStatSurfaceArea"), f.S, 1e-3 * f.S, $"FAR's area of panel {p.craftID} is B9's (m2)");
                Assert.Near((double)Refl.Get(wp, "aeroStatSemispan"), f.Semispan, 1e-4, $"FAR's semispan of panel {p.craftID} is B9's (m)");
                Assert.Near((double)Refl.Get(wp, "aeroStatMeanAerodynamicChord"), f.Mac, 1e-4, $"FAR's mean chord of panel {p.craftID} is B9's (m)");
            }

            Aero twin = Measure();
            yield return Snap.Shot(ctx, "twin");

            ctx.Step("The stock Aeris 4A");
            yield return Nav.LoadCraft(ctx, ctx.CraftPath(Far.Plane, "SPH"));
            // FAR's sim reads ready from the craft before until it has voxelized this one and taken up its wings
            yield return Wait.Until(() => Far.EditorReady && EditorLogic.fetch.ship.parts.Any(p => p.partInfo.name == "deltaWing") &&
                                          Far.Instant(2, 0.5).Cl > 0.01,
                                    120, "FAR's editor sim on the stock craft", shot: false);
            Dictionary<uint, Panel> stock = Panels(EditorLogic.fetch.ship.parts, p => p.partInfo.name == "structuralWing" || p.partInfo.name == "deltaWing");
            Assert.Equal(4, stock.Count, "the stock plane's four main panels");
            Aero real = Measure();

            ctx.Step("Panel for panel, FAR's wing is the stock one");
            foreach (KeyValuePair<uint, Panel> e in stock)
            {
                Assert.True(b9.TryGetValue(e.Key, out Panel t), $"the twin has a panel in place of {e.Value.Part} {e.Key}");
                Panel s = e.Value;
                ctx.Record("stock." + e.Key, s.ToString());
                Assert.Near(s.S, t.S, 0.002 * s.S, $"the area of the panel in place of {s.Part} {e.Key} (m2)");
                Assert.Near(s.Semispan, t.Semispan, 0.002 * s.Semispan, $"the semispan in place of {s.Part} {e.Key} (m)");
                Assert.Near(s.Mac, t.Mac, 0.002 * s.Mac, $"the mean chord in place of {s.Part} {e.Key} (m)");
                Assert.Near(s.Taper, t.Taper, 0.002, $"the taper in place of {s.Part} {e.Key}");
                Assert.Near(s.Sweep, t.Sweep, 0.1, $"the mid-chord sweep in place of {s.Part} {e.Key} (deg)");
            }

            ctx.Step("The whole plane at Mach 0.5");
            for (int i = 0; i < Alphas.Length; i++)
                ctx.Record($"aoa{Alphas[i]}", string.Format(CultureInfo.InvariantCulture, "Cl stock {0:F4} twin {1:F4}, Cd stock {2:F5} twin {3:F5}",
                                                             real.Cl[i], twin.Cl[i], real.Cd[i], twin.Cd[i]));
            double slopeStock = (real.Cl[2] - real.Cl[0]) / 4, slopeTwin = (twin.Cl[2] - twin.Cl[0]) / 4;
            ctx.Record("liftSlope", $"stock {slopeStock:F5}, twin {slopeTwin:F5} per deg");
            Assert.Near(slopeStock, slopeTwin, 0.02 * slopeStock, "the twin's lift slope is the stock plane's");
            for (int i = 0; i < Alphas.Length; i++)
                Assert.Near(real.Cl[i], twin.Cl[i], 0.02 * Math.Abs(real.Cl[i]) + 0.002, $"the twin's Cl at {Alphas[i]} deg");
            for (int i = 0; i < Alphas.Length; i++)
                Assert.Near(real.Cd[i], twin.Cd[i], 0.04 * real.Cd[i], $"the twin's Cd at {Alphas[i]} deg");
        }
    }
}
