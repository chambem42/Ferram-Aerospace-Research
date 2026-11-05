using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ferram4;
using KspTest;
using UnityEngine;

namespace FerramAerospaceResearch.InGameTests
{
    /// <summary>
    /// FAR's math that needs the engine to check against (Unity's curves) or the game's assemblies to load (the wing
    /// model's types), run at the main menu: ExactCurve, the off-thread copy of a Unity curve the parallel sweep
    /// evaluates, and the wing group's span union.
    /// </summary>
    public static class CurveTests
    {
        private static AnimationCurve Curve(params Keyframe[] keys) => new AnimationCurve(keys);

        /// <summary>ExactCurve against Unity's own evaluation of the same curve, at the times given.</summary>
        private static void SameAsUnity(string what, AnimationCurve curve, IEnumerable<float> times, double tol = 1e-5)
        {
            var exact = new ExactCurve(curve);
            foreach (float t in times)
            {
                float unity = curve.Evaluate(t), ours = exact.Evaluate(t);
                Assert.Near(unity, ours, tol * (1 + Math.Abs(unity)), $"{what} at {t}: ExactCurve {ours}, Unity {unity}");
            }
        }

        private static IEnumerable<float> Range(float from, float to, int n) =>
            Enumerable.Range(0, n + 1).Select(i => from + (to - from) * i / n);

        /// <summary>
        /// ExactCurve evaluates as Unity does: no keys (0), one key (its value everywhere), Hermite segments between
        /// keys, a stepped key (an infinite tangent holds the left value), and before and after the keys clamped to the
        /// end values. With a Loop or PingPong wrap, or a weighted key, it says it is not exact, and its wrap still
        /// lands where Unity's does. Matches tells a curve with the same keys and wrap from one with a key changed,
        /// added or a wrap mode changed.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.MainMenu, TimeoutSec = 300)]
        public static IEnumerator Load_ExactCurveIsUnitysCurve(TestContext ctx)
        {
            var empty = new AnimationCurve();
            var none = new ExactCurve(empty);
            Assert.Equal(0f, none.Evaluate(3), "no keys: 0");
            Assert.True(none.IsExact, "no keys: exact");
            Assert.True(float.IsNaN(none.Evaluate(float.NaN)), "NaN in, NaN out");

            AnimationCurve single = Curve(new Keyframe(2, 7));
            SameAsUnity("one key", single, new[] { -5f, 2, 9 });
            Assert.Equal(7f, new ExactCurve(single).Evaluate(100), "one key: its value everywhere");

            AnimationCurve smooth = Curve(new Keyframe(0, 0, 0, 2), new Keyframe(1, 1, 0.5f, -1), new Keyframe(3, -2, 1, 0), new Keyframe(4, 5, 3, 3));
            Assert.True(new ExactCurve(smooth).IsExact, "a plain Hermite curve is exact");
            SameAsUnity("Hermite segments, clamped beyond", smooth, Range(-2, 6, 64));

            var stepKey = new Keyframe(1, 4, float.PositiveInfinity, float.PositiveInfinity);
            AnimationCurve stepped = Curve(new Keyframe(0, 1, 0, float.PositiveInfinity), stepKey, new Keyframe(2, 0));
            SameAsUnity("stepped keys", stepped, Range(0, 1.99f, 20));
            Assert.Equal(1f, new ExactCurve(stepped).Evaluate(0.7f), "a stepped segment holds its left key's value");

            AnimationCurve loop = Curve(new Keyframe(0, 0, 1, 1), new Keyframe(2, 3, -1, -1));
            loop.preWrapMode = loop.postWrapMode = WrapMode.Loop;
            var loopExact = new ExactCurve(loop);
            Assert.False(loopExact.IsExact, "a looping curve is not exact");
            SameAsUnity("Loop wrap", loop, new[] { -3.5f, -1.25f, 2.5f, 3.75f, 5.1f }, 1e-3);

            AnimationCurve pingPong = Curve(new Keyframe(0, 0, 1, 1), new Keyframe(2, 3, -1, -1));
            pingPong.preWrapMode = pingPong.postWrapMode = WrapMode.PingPong;
            Assert.False(new ExactCurve(pingPong).IsExact, "a ping-pong curve is not exact");
            SameAsUnity("PingPong wrap", pingPong, new[] { -3.5f, -1.25f, 2.5f, 3.75f, 5.1f }, 1e-3);

            var weighted = new Keyframe(1, 1, 0, 0, 0.5f, 0.5f) { weightedMode = WeightedMode.Both };
            Assert.False(new ExactCurve(Curve(new Keyframe(0, 0), weighted, new Keyframe(2, 0))).IsExact, "a weighted key is not exact");

            ctx.Step("Matches");
            var copy = new ExactCurve(smooth);
            Assert.True(copy.Matches(smooth), "the curve it was made from");
            Assert.True(copy.Matches(new AnimationCurve(smooth.keys)), "a curve with the same keys");
            Assert.False(copy.Matches(null), "no curve");
            var wrapped = new AnimationCurve(smooth.keys) { postWrapMode = WrapMode.Loop };
            Assert.False(copy.Matches(wrapped), "another wrap mode");
            var longer = new AnimationCurve(smooth.keys);
            longer.AddKey(6, 1);
            Assert.False(copy.Matches(longer), "a key more");
            Keyframe[] moved = smooth.keys;
            moved[2].value += 0.5f;
            Assert.False(copy.Matches(new AnimationCurve(moved)), "a key's value changed");
            yield break;
        }

        private static double MergedLength(float[] lo, float[] hi)
        {
            Type group = typeof(FARWingInteraction).GetNestedType("WingGroup", BindingFlags.NonPublic) ??
                         throw new UITestFailure("FARWingInteraction has no WingGroup");
            MethodInfo m = group.GetMethod("MergedLength", BindingFlags.NonPublic | BindingFlags.Static) ??
                           throw new UITestFailure("WingGroup has no MergedLength");
            return (double)m.Invoke(null, new object[] { lo.ToList(), hi.ToList() });
        }

        /// <summary>
        /// The span a wing group's panels cover (WingGroup.MergedLength): the union of their spanwise intervals, given in
        /// any order: overlapping ones count once, gaps between disjoint ones not at all, one inside another adds
        /// nothing, and no panels cover nothing.
        /// </summary>
        [UITest(Kind = "Check", StartsAt = StartState.MainMenu, TimeoutSec = 300)]
        public static IEnumerator Load_WingSpanIsTheUnionOfItsPanels(TestContext ctx)
        {
            Assert.Near(0, MergedLength(new float[0], new float[0]), 1e-9, "no panels");
            Assert.Near(2, MergedLength(new[] { 1f }, new[] { 3f }), 1e-6, "one panel");
            Assert.Near(4, MergedLength(new[] { 2f, 0f, 1f }, new[] { 4f, 1.5f, 3f }), 1e-6, "overlapping panels out of order: [0, 4]");
            Assert.Near(3, MergedLength(new[] { 5f, 0f }, new[] { 7f, 1f }), 1e-6, "disjoint panels out of order: [0, 1] and [5, 7]");
            Assert.Near(8, MergedLength(new[] { 1f, -2f, 3f }, new[] { 2f, 6f, 4f }), 1e-6, "panels inside another: [-2, 6]");
            Assert.Near(5, MergedLength(new[] { 2f, 0f }, new[] { 5f, 2f }), 1e-6, "panels end to end: [0, 5]");
            yield break;
        }
    }
}
