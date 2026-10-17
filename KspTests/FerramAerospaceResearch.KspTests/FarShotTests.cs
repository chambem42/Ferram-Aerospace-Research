using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using KspTest;
using UnityEngine;

namespace FerramAerospaceResearch.KspTests
{
    /// <summary>Pictures of crafts for FAR's planform figures.</summary>
    public static class FarShotTests
    {
        private static Bounds CraftBounds(List<Part> parts)
        {
            var b = new Bounds(parts[0].transform.position, Vector3.zero);
            foreach (Part p in parts)
                foreach (var r in p.GetComponentsInChildren<Renderer>())
                    if (r.enabled) b.Encapsulate(r.bounds);
            return b;
        }

        /// <summary>
        /// A plan view of each craft in the SPH: a long-lens camera straight down, nose up, the UI hidden and no part
        /// highlighted, the whole craft in frame. Records the scale and where the center of mass lands in the shot, so the
        /// picture can be laid over a planform drawn in FAR's frame (x forward, y right, meters from the center of mass).
        /// Params: crafts ("+"-separated; default the start craft), craft (the start craft).
        /// </summary>
        [UITest("Far_TopShots", StartsAt = StartState.Sph, Craft = "Stearwing A300", CraftDir = "SPH", TimeoutSec = 1800)]
        public static IEnumerator Far_TopShots(TestContext ctx)
        {
            var inv = CultureInfo.InvariantCulture;
            string[] crafts = (ctx.Param("crafts") ?? ctx.Param("craft") ?? "Stearwing A300").Split('+');
            foreach (string craft in crafts)
            {
                ctx.Step("Craft " + craft);
                yield return Nav.EnsureState(ctx, StartState.Sph, ctx.CraftPath(craft, "SPH"));
                Ugui.DismissPopups();
                yield return Wait.Seconds(5);
                var parts = EditorLogic.fetch.ship.parts;
                foreach (Part p in parts)
                {
                    p.SetHighlightType(Part.HighlightType.Disabled);
                    p.SetHighlight(false, false);
                }

                Bounds b = CraftBounds(parts);
                Vector3 nose = Vector3.ProjectOnPlane(EditorLogic.RootPart.transform.up, Vector3.up);
                if (nose.sqrMagnitude < 1e-4f) nose = Vector3.ProjectOnPlane(EditorLogic.RootPart.transform.forward, Vector3.up);
                nose.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, nose).normalized;
                float length = 2 * (Mathf.Abs(b.extents.x * nose.x) + Mathf.Abs(b.extents.z * nose.z));
                float span = 2 * (Mathf.Abs(b.extents.x * right.x) + Mathf.Abs(b.extents.z * right.z));

                // the center of mass, the origin of FAR's editor frame
                Vector3 com = Vector3.zero;
                float mass = 0;
                foreach (Part p in parts)
                {
                    float m = p.mass + p.GetResourceMass();
                    com += m * p.transform.TransformPoint(p.CoMOffset);
                    mass += m;
                }
                com /= Mathf.Max(mass, 1e-6f);

                // the editor's camera controllers recompute the pose every frame: off for the rest of the session
                foreach (var mb in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                    if (mb != null && (mb.GetType().Name == "SPHCamera" || mb.GetType().Name == "VABCamera"))
                        mb.enabled = false;
                Camera unity = EditorLogic.fetch.editorCamera ?? Camera.main;
                float size = 0.55f * Mathf.Max(length, span / unity.aspect);   // half the height in view, m
                // the editor keeps its cameras perspective (an orthographic flag does not stick), so a long lens from far
                // up: 6 deg of view puts the camera 19 craft heights away, near enough to a plan view
                const float fov = 6;
                float dist = size / Mathf.Tan(fov / 2 * Mathf.Deg2Rad);
                Vector3 pos = new Vector3(b.center.x, b.center.y + dist, b.center.z);
                foreach (var c in Camera.allCameras)
                {
                    if (c == null || c.name.StartsWith("UI")) continue;
                    if (c.transform.parent != null) c.transform.SetParent(null, true);
                    c.fieldOfView = fov;
                    c.transform.position = pos;
                    c.transform.rotation = Quaternion.LookRotation(Vector3.down, nose);
                    // a near plane just above the craft leaves out the hangar's roof between it and the camera
                    c.nearClipPlane = Mathf.Max(1, dist - b.extents.y - 5);
                    c.farClipPlane = Mathf.Max(c.farClipPlane, dist + 300);
                }

                // scenery=off: the hangar left out (a craft wider than the hangar has its walls over the wings)
                bool noScenery = ctx.Param("scenery") == "off";
                var hidden = new List<Camera>();
                if (noScenery)
                    foreach (var c in Camera.allCameras)
                        if (c != null && c.name == "sceneryCam") { c.enabled = false; hidden.Add(c); }
                GameEvents.onHideUI.Fire();
                yield return Wait.Seconds(1);
                Vector3 s = unity.WorldToScreenPoint(com);
                Vector3 sr = unity.WorldToScreenPoint(com + right);
                Vector3 sn = unity.WorldToScreenPoint(com + nose);
                // screen coordinates from the top left, px; right and nose: where 1 m to the craft's right and ahead lands
                ctx.Record(craft + ".topshot", string.Format(inv,
                    "screen {0}x{1} ppm {2:F3} com {3:F1} {4:F1} right {5:F2} {6:F2} nose {7:F2} {8:F2} span {9:F2} length {10:F2}",
                    Screen.width, Screen.height, Screen.height / (2 * size), s.x, Screen.height - s.y,
                    sr.x - s.x, s.y - sr.y, sn.x - s.x, s.y - sn.y, span, length));
                foreach (var c in Camera.allCameras)
                    if (c != null)
                        ctx.Record(craft + ".camera." + c.name, string.Format(inv, "enabled {0} fov {1:F1} pos {2} target {3} com {4}",
                            c.enabled, c.fieldOfView, c.transform.position, b.center, com));
                yield return Snap.Shot(ctx, $"{craft}-plan");
                GameEvents.onShowUI.Fire();
                foreach (var c in hidden) c.enabled = true;
            }
        }
    }
}
