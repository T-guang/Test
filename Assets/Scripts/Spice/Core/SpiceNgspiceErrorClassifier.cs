using System;

namespace ElectricalSim.Spice.Core
{
    public static class SpiceNgspiceErrorClassifier
    {
        public static bool TryGetSevereErrorLine(string standardError, out string line)
        {
            line = null;
            foreach (var candidate in (standardError ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
            {
                var trimmed = candidate.Trim();
                if (trimmed.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
                    trimmed.StartsWith("Fatal:", StringComparison.OrdinalIgnoreCase))
                {
                    line = trimmed;
                    return true;
                }
            }
            return false;
        }
    }
}
