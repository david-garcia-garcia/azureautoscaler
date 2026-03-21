using System.Globalization;

namespace poolautoscaler.utils
{
    /// <summary>
    /// Helpers for parsing integer values from strings that may carry floating-point formatting
    /// (e.g. metric or forecast outputs such as "28.000000000000004" or "100.7").
    /// Any decimal value is rounded up to the nearest integer (ceiling).
    /// </summary>
    internal static class IntParseUtils
    {
        /// <summary>
        /// Tries to parse <paramref name="s"/> as an integer.
        /// Clean integer strings (e.g. "200") are parsed directly.
        /// Decimal strings (e.g. "28.000000000000004", "100.7") are parsed as a double and ceiled.
        /// </summary>
        /// <param name="s">The string to parse.</param>
        /// <param name="value">The parsed integer value, or 0 if parsing fails.</param>
        /// <returns>True if <paramref name="s"/> could be parsed; false otherwise.</returns>
        public static bool TryParseInt(string s, out int value)
        {
            value = 0;

            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var d))
            {
                value = (int)Math.Ceiling(d);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Parses <paramref name="s"/> as an integer, ceiling any decimal part.
        /// </summary>
        /// <param name="s">The string to parse.</param>
        /// <returns>The parsed integer value.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="s"/> cannot be parsed as a number.</exception>
        public static int ParseInt(string s)
        {
            if (TryParseInt(s, out var value))
            {
                return value;
            }

            throw new ArgumentException($"Cannot parse '{s}' as an integer.");
        }
    }
}
