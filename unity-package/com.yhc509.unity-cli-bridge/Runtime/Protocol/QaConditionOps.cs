#nullable enable
using System;
using System.Globalization;

namespace UnityCli.Protocol
{
    /// <summary>
    /// Pure operator comparison for qa run-sequence conditions. Values are comma-separated:
    /// scalar ("5"), vector ("3,0,3"), string ("PlayerTurn"), or bool ("true"). Numeric when all
    /// components parse as numbers; otherwise string/bool equality. 'changed' is handled by the
    /// step machine (needs a baseline), not here.
    /// </summary>
    public static class QaConditionOps
    {
        public static bool Evaluate(string actual, string op, string expected, float epsilon)
        {
            if (TryParseNumbers(actual, out double[] a) && TryParseNumbers(expected, out double[] e))
            {
                return EvaluateNumeric(a, op, e, epsilon);
            }

            return op switch
            {
                "!=" => !string.Equals(actual, expected, StringComparison.Ordinal),
                _ => string.Equals(actual, expected, StringComparison.Ordinal),
            };
        }

        public static bool TryParseNumbers(string value, out double[] numbers)
        {
            string[] parts = (value ?? string.Empty).Split(',');
            var result = new double[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (!double.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[i]))
                {
                    numbers = Array.Empty<double>();
                    return false;
                }
            }

            numbers = result;
            return true;
        }

        private static bool EvaluateNumeric(double[] a, string op, double[] e, float epsilon)
        {
            if (a.Length != e.Length)
            {
                return false;
            }

            double tol = op == "near" ? (epsilon > 0f ? epsilon : 0.0) : 0.0;

            switch (op)
            {
                case "==":
                case "near":
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (Math.Abs(a[i] - e[i]) > tol)
                        {
                            return false;
                        }
                    }

                    return true;
                case "!=":
                    for (int i = 0; i < a.Length; i++)
                    {
                        if (Math.Abs(a[i] - e[i]) > 0.0)
                        {
                            return true;
                        }
                    }

                    return false;
                case ">=":
                    return a.Length == 1 && a[0] >= e[0];
                case "<=":
                    return a.Length == 1 && a[0] <= e[0];
                default:
                    return false;
            }
        }
    }
}
