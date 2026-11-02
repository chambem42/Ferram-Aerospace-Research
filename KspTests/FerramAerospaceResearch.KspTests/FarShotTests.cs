using System.Collections;
using KspTest;

namespace FerramAerospaceResearch.KspTests
{
    /// <summary>Pictures of crafts for FAR's planform figures.</summary>
    public static class FarShotTests
    {
        /// <summary>
        /// A plan view of each craft in the SPH (and, by the views param, side and front views): a long-lens camera straight
        /// down, nose up, the UI hidden and no part highlighted, the whole craft in frame. Records the scale and where the
        /// center of mass lands in the shot, so the picture can be laid over a planform drawn in FAR's frame (x forward, y
        /// right, meters from the center of mass) (<see cref="EditorShots.Views" />). Params: crafts ("+"-separated; default
        /// the start craft), craft (the start craft), views ("+"-separated: plan, side, front; default plan), scenery (off:
        /// the hangar hidden).
        /// </summary>
        [UITest("Far_TopShots", Kind = "Measure", StartsAt = StartState.Sph, Craft = "Stearwing A300", CraftDir = "SPH", TimeoutSec = 1800)]
        public static IEnumerator Far_TopShots(TestContext ctx)
        {
            string[] crafts = (ctx.Param("crafts") ?? ctx.Param("craft") ?? "Stearwing A300").Split('+');
            foreach (string craft in crafts)
            {
                ctx.Step("Craft " + craft);
                yield return Nav.EnsureState(ctx, StartState.Sph, ctx.CraftPath(craft, "SPH"));
                Ugui.DismissPopups();
                yield return Wait.Seconds(5);
                yield return EditorShots.Views(ctx, craft, (ctx.Param("views") ?? "plan").Split('+'), ctx.Param("scenery") == "off");
            }
        }
    }
}
