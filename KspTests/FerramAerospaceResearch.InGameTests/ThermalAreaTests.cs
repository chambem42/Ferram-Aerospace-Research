using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FerramAerospaceResearch.FARAeroComponents;
using FerramAerospaceResearch.Settings;
using KspTest;
using UnityEngine;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>The areas FAR gives the game's heating model, from its voxels.</summary>
    public static class ThermalAreaTests
    {
        /// <summary>
        /// With FAR's exposed-area hack off (the config's exposedAreaUsesKSPHack = false), in cruise at Mach 0.5: each
        /// part's radiative area is the surface FAR's voxels give it, its exposed area the part of it facing the flow
        /// (no more than the whole), and its drag cube's occluded areas are FAR's projected areas
        /// face by face.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.Flight, Craft = Far.Plane, CraftDir = "SPH", LaunchSite = "Runway", TimeoutSec = 900)]
        public static IEnumerator Flight_ExposedAreaFromVoxelsWithoutTheKspHack(TestContext ctx)
        {
            yield return Wait.Seconds(5);
            FarFlightProbe.Condition c = Far.At(0.5, 3000, 4);
            yield return FlightTests.Airborne(c);
            Vessel v = FlightGlobals.ActiveVessel;
            bool hackWas = FARSettings.ExposedAreaUsesKSPHack;
            try
            {
                FARSettings.ExposedAreaUsesKSPHack = false;
                yield return FarFlightProbe.Hold(c, 10, 1, new List<FarFlightProbe.Sample>(), predict: false);

                int checkedParts = 0, facingParts = 0;
                foreach (Part p in v.parts)
                {
                    FARAeroPartModule m = p.Modules.GetModule<FARAeroPartModule>();
                    if (m == null || p.DragCubes.None || m.ProjectedAreas.totalArea <= 0)
                        continue;
                    string name = p.partInfo.name;
                    double total = m.ProjectedAreas.totalArea;
                    Assert.Near(total, p.radiativeArea, 1e-4 * total, $"{name}'s radiative area is FAR's voxel surface");
                    Assert.True(p.exposedArea >= 0 && p.exposedArea <= p.radiativeArea * 1.0001,
                                $"{name}'s exposed area within its surface ({p.exposedArea:F4} of {p.radiativeArea:F4} m2)");
                    double facing = m.ProjectedAreaLocal(-p.dragVectorDirLocal);
                    if (facing > 0)
                        Assert.Near(facing, p.exposedArea, 0.05 * facing + 1e-4, $"{name}'s exposed area faces the flow");
                    for (int j = 0; j < 6; j++)
                        Assert.Near(m.ProjectedAreas[j], p.DragCubes.AreaOccluded[FARAeroPartModule.ProjectedArea.FaceMap[j]],
                                    1e-4 * m.ProjectedAreas[j] + 1e-6, $"{name}'s drag cube face {j} is FAR's projected area");
                    checkedParts++;
                    if (p.exposedArea > 0) facingParts++;
                }

                ctx.Record("parts", $"{checkedParts}, {facingParts} with an area facing the flow");
                Assert.True(checkedParts > 5 && facingParts > 5, $"parts checked ({checkedParts}, {facingParts} facing the flow)");
            }
            finally
            {
                FARSettings.ExposedAreaUsesKSPHack = hackWas;
            }
        }
    }
}
