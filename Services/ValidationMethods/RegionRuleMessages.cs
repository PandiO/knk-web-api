using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.ValidationMethods
{
    /// <summary>
    /// KNG-173: a failed RegionContainment / LocationInsideRegion rule shows the rule's own
    /// ErrorMessage, which in the seeds names no place ("The gate's region is not completely inside
    /// the district's region."). This appends one sentence naming what it must be inside, from the
    /// validator's placeholders: <c>Must be inside District "Old Quarter" (region domain_27).</c>
    ///
    /// Only for a real "outside" verdict: a validator result with a failureReason (the check
    /// couldn't run) keeps the validator's own message and never reaches this.
    ///
    /// A missing parent region can't be told apart from "outside": knk-plugin's contains-region and
    /// contains-location endpoints answer 200 "false" when the region doesn't exist in the world
    /// (WgRegionIdTaskHandler.checkRegionContainment logs "parent region not found" and returns
    /// false), so no distinct message is given for it.
    /// </summary>
    public static class RegionRuleMessages
    {
        /// <summary>The names the validators use when the parent entity has no Name: not a name.</summary>
        private static readonly HashSet<string> FallbackNames = new(StringComparer.Ordinal) { "Parent Region", "Region" };

        public static bool AppliesTo(string? validationType) =>
            validationType is "RegionContainment" or "LocationInsideRegion";

        /// <summary>
        /// The rule's message with the "Must be inside ..." sentence appended, or unchanged when the
        /// validator gave no name or region, or the message already names it (also as a
        /// {parentEntityName} placeholder the wizard fills in).
        /// </summary>
        public static string WithParent(FieldValidationRule rule, string? message, IReadOnlyDictionary<string, string>? placeholders)
        {
            var text = (message ?? string.Empty).Trim();
            var hint = ParentSentence(rule, text, placeholders);
            if (hint == null) return message ?? string.Empty;
            if (text.Length == 0) return hint;
            var end = text[^1];
            return (end is '.' or '!' or '?' ? text : text + ".") + " " + hint;
        }

        /// <summary>"Must be inside District "Old Quarter" (region domain_27)." or null.</summary>
        public static string? ParentSentence(FieldValidationRule rule, string message, IReadOnlyDictionary<string, string>? placeholders)
        {
            var name = Placeholder(placeholders, "parentEntityName");
            if (name != null && FallbackNames.Contains(name)) name = null;
            var region = Placeholder(placeholders, "parentRegionId") ?? Placeholder(placeholders, "regionName");
            if (name == null && region == null) return null;

            if (name != null && (Mentions(message, name) || Mentions(message, "{parentEntityName}"))) return null;
            if (name == null && (Mentions(message, region!) || Mentions(message, "{parentRegionId}") || Mentions(message, "{regionName}"))) return null;

            var label = EntityLabel(rule);
            var parts = new List<string>();
            if (label != null) parts.Add(label);
            if (name != null) parts.Add($"\"{name}\"");
            if (parts.Count == 0) return $"Must be inside region {region}.";
            var regionPart = region != null ? $" (region {region})" : string.Empty;
            return $"Must be inside {string.Join(" ", parts)}{regionPart}.";
        }

        /// <summary>The dependency field's label ("District"), else the first segment of DependencyPath, else null.</summary>
        private static string? EntityLabel(FieldValidationRule rule)
        {
            var label = rule.DependsOnField?.Label;
            if (!string.IsNullOrWhiteSpace(label)) return label.Trim();
            var first = rule.DependencyPath?.Split('.', 2)[0];
            return string.IsNullOrWhiteSpace(first) ? null : first.Trim();
        }

        private static string? Placeholder(IReadOnlyDictionary<string, string>? placeholders, string key)
        {
            if (placeholders == null) return null;
            var entry = placeholders.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(entry.Value) ? null : entry.Value.Trim();
        }

        private static bool Mentions(string message, string value) =>
            message.Contains(value, StringComparison.OrdinalIgnoreCase);
    }
}
