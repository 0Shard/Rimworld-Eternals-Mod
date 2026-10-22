using System;
using System.Globalization;
using System.Numerics;

namespace Eternal.Resources
{
    // Every finite food-float value is an integer multiple of 2^-149 nutrition.
    // Keeping those units avoids losing food payments or living headroom beside huge debt.
    internal readonly struct NutritionAmount : IEquatable<NutritionAmount>, IFormattable
    {
        private readonly BigInteger units;
        private NutritionAmount(BigInteger units) { this.units = units; }
        internal bool IsZero => units.IsZero;

        internal static bool IsSupported(float value) => value >= 0f && !float.IsInfinity(value);

        internal static NutritionAmount FromFloat(float value)
        {
            if (!IsSupported(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Nutrition must be finite and nonnegative.");
            }
            int bits = BitConverter.ToInt32(BitConverter.GetBytes(value), 0);
            int exponent = (bits >> 23) & 255;
            int significand = bits & 0x7fffff;
            if (exponent > 0)
            {
                significand |= 0x800000;
            }
            return new NutritionAmount(new BigInteger(significand) << Math.Max(0, exponent - 1));
        }

        internal static NutritionAmount Product(float first, float second)
            => new NutritionAmount((FromFloat(first).units * FromFloat(second).units) >> 149);

        internal float ToFloat()
        {
            float result = (float)((double)units * Math.Pow(2d, -149d));
            if (float.IsInfinity(result))
            {
                return float.MaxValue;
            }
            // Budgets and returned settlements may never round above the exact amount.
            if (FromFloat(result).units > units)
            {
                int bits = BitConverter.ToInt32(BitConverter.GetBytes(result), 0);
                result = BitConverter.ToSingle(BitConverter.GetBytes(bits - 1), 0);
            }
            return result;
        }

        internal string Serialize() => units.ToString(CultureInfo.InvariantCulture);

        internal static bool TryParse(string text, out NutritionAmount amount)
        {
            amount = default;
            // Bound corrupt input before allocating a BigInteger. This permits over 10^466
            // nutrition, far beyond finite-float prices and any feasible accumulation.
            if (string.IsNullOrEmpty(text) || text.Length > 512)
            {
                return false;
            }
            foreach (char digit in text)
            {
                if (digit < '0' || digit > '9')
                {
                    return false;
                }
            }
            if (!BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                return false;
            }
            amount = new NutritionAmount(parsed);
            return true;
        }

        internal static NutritionAmount Min(NutritionAmount first, NutritionAmount second)
            => first.units <= second.units ? first : second;
        internal static NutritionAmount Max(NutritionAmount first, NutritionAmount second)
            => first.units >= second.units ? first : second;
        internal static NutritionAmount Difference(NutritionAmount first, NutritionAmount second)
            => new NutritionAmount(BigInteger.Max(BigInteger.Zero, first.units - second.units));
        public static NutritionAmount operator +(NutritionAmount first, NutritionAmount second)
            => new NutritionAmount(first.units + second.units);
        public bool Equals(NutritionAmount other) => units.Equals(other.units);
        public override bool Equals(object obj) => obj is NutritionAmount amount && Equals(amount);
        public override int GetHashCode() => units.GetHashCode();
        public string ToString(string format, IFormatProvider provider) => ToFloat().ToString(format, provider);
        public override string ToString() => ToString(null, CultureInfo.InvariantCulture);
    }
}
