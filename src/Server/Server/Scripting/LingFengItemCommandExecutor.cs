using System;

namespace Server.Scripting
{
    public static class LingFengItemCommandExecutor
    {
        public static bool NameMatches(string itemName, string requestedName, bool partial)
        {
            if (string.IsNullOrWhiteSpace(itemName) || string.IsNullOrWhiteSpace(requestedName)) return false;
            return partial
                ? itemName.Contains(requestedName, StringComparison.OrdinalIgnoreCase)
                : itemName.Equals(requestedName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool DurabilityMatches(ushort current, ushort maximum, int mode) => mode switch
        {
            0 => true,
            -1 => maximum > 0 && current >= maximum,
            -2 => maximum > 0 && current < maximum,
            _ => false
        };
    }
}
