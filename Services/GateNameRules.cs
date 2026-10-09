using System;
using System.Collections.Generic;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Naming rules shared by gate structures and gate doors.
    /// </summary>
    /// <remarks>
    /// KNG-78: the plugin's gate commands accept the keyword <c>here</c> in place of a gate
    /// structure or gate door id/name (<c>/gate toggle here</c>, <c>/gatedoor repair here</c>), so
    /// those keywords can't be used as names. Keep this list in sync with the plugin's keywords.
    /// </remarks>
    public static class GateNameRules
    {
        public static readonly IReadOnlyCollection<string> ReservedNames = new[] { "here" };

        /// <summary>True when the name, trimmed and compared case-insensitively, is a reserved keyword.</summary>
        public static bool IsReserved(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var trimmed = name.Trim();
            foreach (var reserved in ReservedNames)
            {
                if (string.Equals(trimmed, reserved, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>Throws an <see cref="ArgumentException"/> (mapped to 400 by the controllers) for a reserved name.</summary>
        /// <param name="name">The requested name.</param>
        /// <param name="entityLabel">"gate structure" or "gate door", used in the message.</param>
        /// <param name="commandHint">The command family that reserves it, e.g. "/gate" or "/gatedoor".</param>
        /// <param name="paramName">The argument name reported on the exception.</param>
        public static void EnsureNotReserved(string? name, string entityLabel, string commandHint, string paramName)
        {
            if (!IsReserved(name)) return;
            var keyword = name!.Trim().ToLowerInvariant();
            throw new ArgumentException(
                $"'{keyword}' is reserved by the gate commands ({commandHint} … {keyword}) and can't be used as a {entityLabel} name.",
                paramName);
        }
    }
}
