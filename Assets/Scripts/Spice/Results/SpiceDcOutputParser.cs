using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using ElectricalSim.Spice.Netlist;

namespace ElectricalSim.Spice.Results
{
    /// <summary>
    /// 只解析 T2 标记之间的明确 print 输出，避免将 ngspice 普通日志误当作电路数值。
    /// </summary>
    public static class SpiceDcOutputParser
    {
        // BJT 仅把 ngspice 的 @q[ic] 作为集电极电流读入；大小写按 ngspice 回显兼容，不能把 ib/ie 或 AC 小信号输出混入该 DC 契约。
        private static readonly Regex ValuePattern = new Regex(@"^\s*(?<kind>[vi])\s*\(\s*(?<id>[^)]+)\s*\)\s*=\s*(?<value>[+-]?(?:(?:\d+(?:\.\d*)?)|(?:\.\d+))(?:[eEdD][+-]?\d+)?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex DiodeCurrentPattern = new Regex(@"^\s*@(?<id>[^\[]+)\[id\]\s*=\s*(?<value>[+-]?(?:(?:\d+(?:\.\d*)?)|(?:\.\d+))(?:[eEdD][+-]?\d+)?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex BjtCollectorCurrentPattern = new Regex(@"^\s*@(?<id>[^\[]+)\[ic\]\s*=\s*(?<value>[+-]?(?:(?:\d+(?:\.\d*)?)|(?:\.\d+))(?:[eEdD][+-]?\d+)?)\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool TryParse(string standardOutput, out Dictionary<string, double> nodeVoltages, out Dictionary<string, double> branchCurrents, out string failure)
        {
            nodeVoltages = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            branchCurrents = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            failure = null;
            var begin = (standardOutput ?? string.Empty).IndexOf(SpiceNetlistBuilder.BeginMarker, StringComparison.OrdinalIgnoreCase);
            var end = begin < 0 ? -1 : standardOutput.IndexOf(SpiceNetlistBuilder.EndMarker, begin + SpiceNetlistBuilder.BeginMarker.Length, StringComparison.OrdinalIgnoreCase);
            if (begin < 0 || end <= begin) { failure = "T2 output markers were not found."; return false; }
            var marked = standardOutput.Substring(begin + SpiceNetlistBuilder.BeginMarker.Length, end - begin - SpiceNetlistBuilder.BeginMarker.Length);
            foreach (Match match in ValuePattern.Matches(marked))
            {
                var number = match.Groups["value"].Value.Replace('D', 'E').Replace('d', 'e');
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) { failure = "Unable to parse ngspice value '" + match.Groups["value"].Value + "'."; return false; }
                var id = match.Groups["id"].Value.Trim();
                if (string.Equals(match.Groups["kind"].Value, "v", StringComparison.OrdinalIgnoreCase)) nodeVoltages[id] = value;
                else branchCurrents[id] = value;
            }
            foreach (Match match in DiodeCurrentPattern.Matches(marked))
            {
                var number = match.Groups["value"].Value.Replace('D', 'E').Replace('d', 'e');
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) { failure = "Unable to parse diode current."; return false; }
                branchCurrents[match.Groups["id"].Value.Trim()] = value;
            }
            foreach (Match match in BjtCollectorCurrentPattern.Matches(marked))
            {
                var number = match.Groups["value"].Value.Replace('D', 'E').Replace('d', 'e');
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) { failure = "Unable to parse BJT collector current."; return false; }
                branchCurrents[match.Groups["id"].Value.Trim()] = value;
            }
            return true;
        }
    }
}
