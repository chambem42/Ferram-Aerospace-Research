using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace FerramAerospaceResearch
{
    internal class GenericValueEqualityComparer<T> : EqualityComparer<T> where T : struct, IEquatable<T>
    {
        public override bool Equals(T x, T y) => x.Equals(y);
        // coverage: override nothing calls (only Equals(x, y) is used)
        [ExcludeFromCodeCoverage]
        public override int GetHashCode(T obj) => obj.GetHashCode();
        // coverage: override nothing calls (only Equals(x, y) is used)
        [ExcludeFromCodeCoverage]
        public override bool Equals(object obj) => obj is GenericValueEqualityComparer<T>;
        // coverage: override nothing calls (only Equals(x, y) is used)
        [ExcludeFromCodeCoverage]
        public override int GetHashCode() => GetType().Name.GetHashCode();
    }

    internal static class CustomEqualityComparer<T>
    {
        public static EqualityComparer<T> Default { get; } = CreateComparer();

        private static EqualityComparer<T> CreateComparer()
        {
            Type t = typeof(T);

            // handle equatable value types differently since C# boxes them for comparisons, enums are compared without boxing
            if (!typeof(IEquatable<T>).IsAssignableFrom(t) || !t.IsValueType || t.IsEnum)
                return EqualityComparer<T>.Default;

            // the workarounds that are needed since C# doesn't support overloads by constraints...
            Type comparerType = typeof(GenericValueEqualityComparer<>).MakeGenericType(t);
            return (EqualityComparer<T>)Activator.CreateInstance(comparerType);
        }
    }

    public static class FARUtils
    {
        public static bool ValuesEqual<T>(T a, T b)
        {
            return CustomEqualityComparer<T>.Default.Equals(a, b);
        }

        // coverage: nothing in FAR calls it (FARMathUtil.Swap is used)
        [ExcludeFromCodeCoverage]
        public static void Swap<T>(ref T a, ref T b)
        {
            (b, a) = (a, b);
        }
    }
}
