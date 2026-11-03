using System;
using System.Collections;
using System.Linq;
using FerramAerospaceResearch.FARAeroComponents;
using FerramAerospaceResearch.FARGUI.FAREditorGUI.Simulation;
using KspTest;
using UnityEngine;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>
    /// FAR in the editor, on stock crafts: the voxel model of the craft and the static analysis built on it, checked
    /// against the craft's own size and against what any conventional plane does (lift rises with angle of attack, it
    /// is stable in pitch, a symmetric craft is symmetric in sideslip, drag rises through Mach 1).
    /// </summary>
    public static class EditorTests
    {
        private const string Plane = "Aeris 4A";

        /// <summary>The voxel model's length and largest cross-section against the craft's bounds.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = Plane, TimeoutSec = 900)]
        public static IEnumerator Editor_VoxelModelMatchesTheCraft(TestContext ctx)
        {
            yield return Far.WaitForEditorVoxels();
            VehicleAerodynamics vehicle = Far.EditorVehicle;
            // FAR measures along the craft's axis, the root part's up for a plane: its length, and the area across it
            Transform root = EditorLogic.RootPart.transform;
            Vector3 extent = CraftExtent(root.up, root.right, root.forward);
            double boundsLength = extent.x, boundsArea = extent.y * extent.z;
            ctx.Record("extent", extent.ToString("F2"));
            ctx.Record("voxelLength", vehicle.Length);
            ctx.Record("maxCrossSection", vehicle.MaxCrossSectionArea);
            Assert.InRange(0.85 * boundsLength, 1.1 * boundsLength, vehicle.Length, "voxel length against the craft's length");
            Assert.InRange(0.02 * boundsArea, boundsArea, vehicle.MaxCrossSectionArea, "largest cross-section inside the craft's bounding box");

            double[] areas = vehicle.GetCrossSectionAreas();
            Assert.True(areas.Length > 10, $"cross-sections along the craft ({areas.Length})");
            Assert.True(areas.All(a => a >= 0 && !double.IsNaN(a)), "every cross-section is a real, non-negative area");
            // MaxCrossSectionArea is the raw voxel's; the sections are smoothed for area ruling after, which lowers peaks
            Assert.InRange(0.7 * vehicle.MaxCrossSectionArea, 1.0001 * vehicle.MaxCrossSectionArea, areas.Max(),
                           "the smoothed sections' largest against the voxel's largest cross-section");
        }

        /// <summary>Lift against angle of attack at Mach 0.4: rising, with a plausible slope, and drag always positive.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = Plane, TimeoutSec = 900)]
        public static IEnumerator Editor_LiftRisesWithAngleOfAttack(TestContext ctx)
        {
            yield return Far.WaitForEditorVoxels();
            double prevCl = double.NegativeInfinity;
            for (int alpha = -4; alpha <= 10; alpha += 2)
            {
                InstantConditionSimOutput o = Far.Instant(alpha, 0.4);
                ctx.Record($"alpha{alpha}", $"Cl {o.Cl:F4} Cd {o.Cd:F4} Cm {o.Cm:F4}");
                Assert.True(o.Cl > prevCl, $"Cl rises with alpha: {o.Cl:F4} at {alpha} deg after {prevCl:F4}");
                Assert.True(o.Cd > 0, $"Cd is positive at {alpha} deg ({o.Cd:F4})");
                prevCl = o.Cl;
            }

            double slopePerDeg = (Far.Instant(4, 0.4).Cl - Far.Instant(0, 0.4).Cl) / 4;
            ctx.Record("liftSlopePerDeg", slopePerDeg);
            // a wing's 2 pi per radian is 0.11 per degree; a whole plane, on its wing area, has rather less
            Assert.InRange(0.02, 0.12, slopePerDeg, "lift slope per degree");
        }

        /// <summary>Nose-down pitching moment rises with angle of attack: the stock plane is stable in pitch.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = Plane, TimeoutSec = 900)]
        public static IEnumerator Editor_StableInPitch(TestContext ctx)
        {
            yield return Far.WaitForEditorVoxels();
            double cm0 = Far.Instant(0, 0.4).Cm, cm8 = Far.Instant(8, 0.4).Cm;
            ctx.Record("Cm0", cm0);
            ctx.Record("Cm8", cm8);
            Assert.True(cm8 < cm0, $"Cm falls with alpha (dCm/dalpha < 0): {cm0:F4} at 0 deg, {cm8:F4} at 8 deg");
        }

        /// <summary>A symmetric craft: no side force, yaw or roll without sideslip, and equal and opposite ones with it.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = Plane, TimeoutSec = 900)]
        public static IEnumerator Editor_SymmetricInSideslip(TestContext ctx)
        {
            yield return Far.WaitForEditorVoxels();
            InstantConditionSimOutput zero = Far.Instant(3, 0.4, 0), right = Far.Instant(3, 0.4, 5), left = Far.Instant(3, 0.4, -5);
            ctx.Record("beta+5", $"Cy {right.Cy:F5} Cn {right.Cn:F5} Croll {right.C_roll:F5}");
            ctx.Record("beta-5", $"Cy {left.Cy:F5} Cn {left.Cn:F5} Croll {left.C_roll:F5}");
            double scale = Math.Abs(right.Cy) + 1e-3;
            Assert.True(Math.Abs(zero.Cy) < 0.02 * scale && Math.Abs(zero.Cn) < 1e-3 && Math.Abs(zero.C_roll) < 1e-3,
                        $"no lateral force or moment at zero sideslip: Cy {zero.Cy:G3}, Cn {zero.Cn:G3}, Croll {zero.C_roll:G3}");
            Assert.True(Math.Abs(right.Cy) > 1e-3, $"sideslip gives a side force ({right.Cy:G3})");
            Assert.Near(-right.Cy, left.Cy, 0.02 * Math.Abs(right.Cy), "side force is equal and opposite either side");
            Assert.Near(-right.Cn, left.Cn, 0.02 * Math.Abs(right.Cn) + 1e-5, "yawing moment is equal and opposite either side");
            Assert.Near(-right.C_roll, left.C_roll, 0.02 * Math.Abs(right.C_roll) + 1e-5, "rolling moment is equal and opposite either side");
        }

        /// <summary>Drag rises through the transonic: wave drag at Mach 1.1 on top of the subsonic drag at Mach 0.5.</summary>
        [UITest(Kind = "Check", StartsAt = StartState.Sph, Craft = Plane, TimeoutSec = 900)]
        public static IEnumerator Editor_TransonicDragRise(TestContext ctx)
        {
            yield return Far.WaitForEditorVoxels();
            double sub = Far.Instant(2, 0.5).Cd, trans = Far.Instant(2, 1.1).Cd;
            ctx.Record("Cd0.5", sub);
            ctx.Record("Cd1.1", trans);
            Assert.True(trans > 1.3 * sub, $"Cd at Mach 1.1 ({trans:F4}) well above Mach 0.5's ({sub:F4})");
        }

        /// <summary>The craft's size along three axes: the spread of its meshes' vertices (in world space) along each.</summary>
        private static Vector3 CraftExtent(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (Part p in EditorLogic.fetch.ship.parts)
                foreach (MeshFilter mf in p.GetComponentsInChildren<MeshFilter>())
                {
                    if (mf.sharedMesh == null || mf.GetComponent<Renderer>()?.enabled != true) continue;
                    foreach (Vector3 local in mf.sharedMesh.vertices)
                    {
                        Vector3 w = mf.transform.TransformPoint(local);
                        var d = new Vector3(Vector3.Dot(w, a), Vector3.Dot(w, b), Vector3.Dot(w, c));
                        min = Vector3.Min(min, d);
                        max = Vector3.Max(max, d);
                    }
                }

            return max - min;
        }
    }
}
