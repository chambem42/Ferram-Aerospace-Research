/*
Ferram Aerospace Research
=========================
Thread-safe densely-sampled copy of a KSP FloatCurve for the parallel editor sweep.

   This file is part of Ferram Aerospace Research.

   Ferram Aerospace Research is free software: you can redistribute it and/or modify
   it under the terms of the GNU General Public License as published by
   the Free Software Foundation, either version 3 of the License, or
   (at your option) any later version.
 */

using UnityEngine;

// ReSharper disable once CheckNamespace
namespace ferram4
{
    /// <summary>
    /// A read-only, densely-sampled approximation of a KSP <see cref="FloatCurve"/> (which is backed by
    /// a Unity <see cref="AnimationCurve"/>). <c>AnimationCurve.Evaluate</c> mutates an internal cache
    /// on every call and is not safe to evaluate concurrently on the same object, so the parallel
    /// heatmap sweep samples the shared curves once on the main thread into these tables and evaluates
    /// them from worker threads instead. Backed by a single immutable array, so concurrent reads are
    /// safe. Interpolation is linear between samples — with a dense grid the drift from the curve's own
    /// interpolation is negligible for the heatmap.
    /// </summary>
    public sealed class SampledCurve
    {
        private readonly float min;
        private readonly float max;
        private readonly float invStep;
        private readonly float[] samples;

        public SampledCurve(FloatCurve curve, float min, float max, int samplesCount)
        {
            if (samplesCount < 2)
                samplesCount = 2;

            this.min = min;
            this.max = max;
            samples = new float[samplesCount];

            float step = (max - min) / (samplesCount - 1);
            invStep = 1f / step;
            for (int i = 0; i < samplesCount; i++)
                samples[i] = curve.Evaluate(min + step * i);
        }

        public float Evaluate(float x)
        {
            if (x <= min)
                return samples[0];
            if (x >= max)
                return samples[samples.Length - 1];

            float f = (x - min) * invStep;
            var i = (int)f;
            float frac = f - i;
            return samples[i] * (1f - frac) + samples[i + 1] * frac;
        }
    }
}
