using System;
using Xunit;

namespace FerramAerospaceResearch.UnitTests
{
    /// <summary>FARMathUtil: comparisons, interpolation, means and elliptic integrals, the root finders.</summary>
    public class MathUtilTests
    {
        [Theory]
        [InlineData(1.0, 1.0, true)]
        [InlineData(1.0, 1.0 + 1e-15, true)]
        [InlineData(1.0, 1.0001, false)]
        [InlineData(-3.0, -3.0, true)]
        [InlineData(0.0, 1e-300, false)]
        [InlineData(double.PositiveInfinity, double.PositiveInfinity, true)]
        [InlineData(double.NaN, double.NaN, false)]
        public void NearlyEqualDouble(double a, double b, bool equal)
        {
            Assert.Equal(equal, a.NearlyEqual(b));
            Assert.Equal(equal, b.NearlyEqual(a));
        }

        [SkippableFact]
        public void NearlyEqualFloat()
        {
            UnityRuntime.Require();   // Mathf
            Assert.True(1f.NearlyEqual(1f + 1e-7f));
            Assert.False(1f.NearlyEqual(1.001f));
        }

        [Fact]
        public void LerpIsTheLineThroughTwoPoints()
        {
            Assert.Equal(25, FARMathUtil.Lerp(0, 10, 0, 100, 2.5), 12);
            Assert.Equal(-50, FARMathUtil.Lerp(0, 10, 0, 100, -5), 12);   // extrapolates
            Assert.Equal(7, FARMathUtil.Lerp(2, 4, 7, 7, 3), 12);
        }

        [Fact]
        public void ClampAndSwap()
        {
            Assert.Equal(1.0, 5.0.Clamp(0.0, 1.0));
            Assert.Equal(0.0, (-2.0).Clamp(0.0, 1.0));
            Assert.Equal(0.5, 0.5.Clamp(0.0, 1.0));
            Assert.Equal(3, 3.Clamp(1, 10));
            double a = 1, b = 2;
            FARMathUtil.Swap(ref a, ref b);
            Assert.Equal((2.0, 1.0), (a, b));
        }

        [Theory]
        [InlineData(1.0, 1.0 + 1e-6, false, true)]
        [InlineData(1.0, 1.1, false, false)]
        [InlineData(1e-9, 0.0, false, true)]               // within the absolute tolerance
        [InlineData(double.NaN, double.NaN, false, false)]
        [InlineData(double.NaN, double.NaN, true, true)]
        [InlineData(double.PositiveInfinity, double.PositiveInfinity, false, true)]
        [InlineData(double.PositiveInfinity, double.NegativeInfinity, false, false)]
        public void IsClose(double a, double b, bool equalNan, bool close)
        {
            Assert.Equal(close, FARMathUtil.IsClose(a, b, equalNan: equalNan));
        }

        [Fact]
        public void IsFinite()
        {
            Assert.True(FARMathUtil.IsFinite(1e308));
            Assert.False(FARMathUtil.IsFinite(double.NaN));
            Assert.False(FARMathUtil.IsFinite(double.NegativeInfinity));
        }

        [Theory]
        [InlineData(2.0, 0.5)]
        [InlineData(10.0, 2.0)]
        [InlineData(0.3, 2.2)]
        [InlineData(8.0, 0.7)]
        public void PowApproxIsWithinAFewPercentOfPow(double a, double b)
        {
            Assert.InRange(FARMathUtil.PowApprox(a, b) / Math.Pow(a, b), 0.9, 1.1);
        }

        [Fact]
        public void BrentFindsARootInsideTheBracket()
        {
            FARMathUtil.OptimizationResult r = FARMathUtil.BrentsMethod(x => x * x - 2, 0, 2, 1e-10);
            Assert.True(r.Converged);
            Assert.Equal(Math.Sqrt(2), r.Result, 8);
        }

        [SkippableFact]
        public void BrentGivesUpOnABracketWithoutASignChange()
        {
            UnityRuntime.RequireLog();   // FAR's logger, if its level lets the message through
            FARMathUtil.OptimizationResult r = FARMathUtil.BrentsMethod(x => x * x + 1, -1, 1);
            Assert.False(r.Converged);
            Assert.Equal(2, r.FunctionCalls);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(3.0)]
        [InlineData(2.5)]
        public void SecantFindsTheRootNearTheGuess(double guess)
        {
            FARMathUtil.OptimizationResult r = FARMathUtil.Secant(x => x * x * x - 2 * x - 5, guess, tol: 1e-12, rTol: 1e-12);   // Wallis's cubic
            Assert.True(r.Converged);
            Assert.Equal(2.0945514815423265, r.Result, 9);
        }

        [Fact]
        public void SecantRefusesTwoEqualGuesses()
        {
            Assert.Throws<ArgumentException>(() => FARMathUtil.Secant(x => x, 1, 1));
        }

        [Fact]
        public void SecantStopsAtItsLimits()
        {
            // no root: the iterates run off past the limit and it gives up rather than loop to maxIter
            FARMathUtil.OptimizationResult r = FARMathUtil.Secant(x => Math.Exp(x), 0, maxLimit: 5, minLimit: -5);
            Assert.False(r.Converged);
            Assert.True(r.FunctionCalls < 50);
        }
    }
}
