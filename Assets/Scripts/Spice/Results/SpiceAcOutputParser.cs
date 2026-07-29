using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ElectricalSim.Spice.Netlist;

namespace ElectricalSim.Spice.Results
{
    public enum SpiceAcParseFailure
    {
        None,
        MarkersMissing,
        MarkersDuplicate,
        MarkersOutOfOrder,
        MalformedLine,
        UnexpectedExpression,
        DuplicateExpression,
        RequiredExpressionMissing,
        NonFiniteValue
    }

    public static class SpiceAcOutputParser
    {
        private static readonly Regex ValueLinePattern = new Regex(
            @"^\s*(?<expression>[vi]\s*\(\s*[^)]+\s*\))\s*=\s*(?<real>[^,]+?)\s*,\s*(?<imaginary>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex NumericPattern = new Regex(
            @"^[+-]?(?:(?:\d+(?:\.\d*)?)|(?:\.\d+))(?:[eEdD][+-]?\d+)?$",
            RegexOptions.CultureInvariant);

        public static bool TryParse(string standardOutput, IReadOnlyList<SpiceAcOutputRequest> expectedRequests,
            out Dictionary<string, SpicePhasor> valuesByResultKey, out SpiceAcParseFailure failure, out string failureMessage)
        {
            valuesByResultKey = new Dictionary<string, SpicePhasor>(StringComparer.Ordinal);
            failure = SpiceAcParseFailure.None;
            failureMessage = null;
            if (expectedRequests == null) throw new ArgumentNullException(nameof(expectedRequests));

            var output = standardOutput ?? string.Empty;
            var beginCount = CountOccurrences(output, SpiceAcNetlistBuilder.BeginMarker);
            var endCount = CountOccurrences(output, SpiceAcNetlistBuilder.EndMarker);
            if (beginCount == 0 || endCount == 0)
                return Fail(SpiceAcParseFailure.MarkersMissing, "AC output markers were not found.", out failure, out failureMessage);
            if (beginCount != 1 || endCount != 1)
                return Fail(SpiceAcParseFailure.MarkersDuplicate, "AC output markers must appear exactly once.", out failure, out failureMessage);

            var begin = output.IndexOf(SpiceAcNetlistBuilder.BeginMarker, StringComparison.OrdinalIgnoreCase);
            var end = output.IndexOf(SpiceAcNetlistBuilder.EndMarker, StringComparison.OrdinalIgnoreCase);
            if (end <= begin)
                return Fail(SpiceAcParseFailure.MarkersOutOfOrder, "AC output end marker appears before the begin marker.", out failure, out failureMessage);

            var expectedByExpression = new Dictionary<string, SpiceAcOutputRequest>(StringComparer.OrdinalIgnoreCase);
            var expectedResultKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var request in expectedRequests)
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Expression) || string.IsNullOrWhiteSpace(request.ResultKey))
                    throw new ArgumentException("AC output requests must be complete.", nameof(expectedRequests));
                var expression = NormalizeExpression(request.Expression);
                if (expectedByExpression.ContainsKey(expression))
                    throw new ArgumentException("AC output request expressions must be unique.", nameof(expectedRequests));
                if (!expectedResultKeys.Add(request.ResultKey))
                    throw new ArgumentException("AC output request result keys must be unique.", nameof(expectedRequests));
                expectedByExpression.Add(expression, request);
            }

            var marked = output.Substring(begin + SpiceAcNetlistBuilder.BeginMarker.Length, end - begin - SpiceAcNetlistBuilder.BeginMarker.Length);
            var seenExpressions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = marked.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (line.Length == 0) continue;
                var match = ValueLinePattern.Match(line);
                if (!match.Success)
                    return Fail(SpiceAcParseFailure.MalformedLine, "AC output contains an unrecognized data line.", out failure, out failureMessage);

                var expression = NormalizeExpression(match.Groups["expression"].Value);
                if (!expectedByExpression.TryGetValue(expression, out var request))
                    return Fail(SpiceAcParseFailure.UnexpectedExpression, "AC output contains an expression that was not requested.", out failure, out failureMessage);
                if (!seenExpressions.Add(expression))
                    return Fail(SpiceAcParseFailure.DuplicateExpression, "AC output contains a duplicate expression.", out failure, out failureMessage);
                if (!TryParseFinite(match.Groups["real"].Value, out var real) || !TryParseFinite(match.Groups["imaginary"].Value, out var imaginary))
                    return Fail(SpiceAcParseFailure.NonFiniteValue, "AC output contains a non-finite complex value.", out failure, out failureMessage);

                try
                {
                    valuesByResultKey.Add(request.ResultKey, new SpicePhasor(real, imaginary));
                }
                catch (ArgumentOutOfRangeException)
                {
                    return Fail(SpiceAcParseFailure.NonFiniteValue, "AC output contains a non-finite complex value.", out failure, out failureMessage);
                }
            }

            if (seenExpressions.Count != expectedByExpression.Count)
                return Fail(SpiceAcParseFailure.RequiredExpressionMissing, "AC output did not contain every requested expression.", out failure, out failureMessage);
            return true;
        }

        public static string NormalizeExpression(string expression)
        {
            if (expression == null) return string.Empty;
            return new string(expression.Where(character => !char.IsWhiteSpace(character)).ToArray()).ToLowerInvariant();
        }

        private static bool TryParseFinite(string text, out double value)
        {
            value = 0d;
            var normalized = (text ?? string.Empty).Trim().Replace('D', 'E').Replace('d', 'e');
            if (!NumericPattern.IsMatch(normalized)) return false;
            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static int CountOccurrences(string text, string marker)
        {
            var count = 0;
            var index = 0;
            while ((index = text.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += marker.Length;
            }
            return count;
        }

        private static bool Fail(SpiceAcParseFailure parseFailure, string message, out SpiceAcParseFailure failure, out string failureMessage)
        {
            failure = parseFailure;
            failureMessage = message;
            return false;
        }
    }
}
