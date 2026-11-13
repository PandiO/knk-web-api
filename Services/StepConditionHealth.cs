using System.Text.Json;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Configuration health for step conditions (PandiO/knk-workspace#88): a rule of a condition that reads a
    /// field which is not on its step (or, for "from an earlier step", not on any earlier step) can never
    /// hold, so the form wizard would refuse Next for ever. Reported as a warning in the builder's health
    /// panel; it does not stop a save or a publish, since the builder flags the row itself.
    /// Only JSON in the shape the builder writes ({ "conditions": [ { "fieldName", "operator", "value",
    /// "fromPreviousStep" } ], "logic" }) is judged; hand-written JSON is left alone.
    /// </summary>
    public static class StepConditionHealth
    {
        public sealed record ConditionInfo(string ConditionType, string? ConditionJson, bool IsActive);

        public sealed record StepInfo(string StepName, IReadOnlyCollection<string> FieldNames, IReadOnlyList<ConditionInfo> Conditions);

        public static List<ValidationIssueDto> FindIssues(FormConfiguration config) =>
            FindIssues(config.Steps.Select(step => new StepInfo(
                step.StepName,
                step.Fields.Select(f => f.FieldName).ToList(),
                step.StepConditions
                    .Select(c => new ConditionInfo(c.ConditionType == "CompletionCondition" ? "Completion" : "Entry", c.ConditionLogicJson, c.IsActive))
                    .ToList())).ToList());

        public static List<ValidationIssueDto> FindIssues(FormConfigurationDto config) =>
            FindIssues((config.Steps ?? new()).Select(step => new StepInfo(
                step.StepName,
                (step.Fields ?? new()).Select(f => f.FieldName).ToList(),
                (step.Conditions ?? new())
                    .Select(c => new ConditionInfo(c.ConditionType, c.ConditionJson, c.IsActive))
                    .ToList())).ToList());

        public static List<ValidationIssueDto> FindIssues(IReadOnlyList<StepInfo> steps)
        {
            var issues = new List<ValidationIssueDto>();
            var earlierFieldNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var step in steps)
            {
                var ownFieldNames = new HashSet<string>(step.FieldNames.Where(n => !string.IsNullOrEmpty(n)), StringComparer.Ordinal);

                foreach (var condition in step.Conditions)
                {
                    foreach (var (fieldName, fromPreviousStep) in ReadRules(condition.ConditionJson))
                    {
                        if ((fromPreviousStep ? earlierFieldNames : ownFieldNames).Contains(fieldName)) continue;

                        var when = condition.ConditionType == "Completion" ? "before leaving" : "before entering";
                        var where = fromPreviousStep ? "on an earlier step" : "on this step";
                        issues.Add(new ValidationIssueDto
                        {
                            Severity = condition.IsActive ? "Warning" : "Info",
                            Message = $"Step '{step.StepName}': a \"{when}\" condition reads the field '{fieldName}', which is not {where}. "
                                + (condition.IsActive
                                    ? "The condition can never hold; pick another field or remove the rule."
                                    : "The condition is switched off, so the form skips it.")
                        });
                    }
                }

                earlierFieldNames.UnionWith(ownFieldNames);
            }

            return issues;
        }

        /// <summary>The rules of a condition in the builder's shape; nothing for any other JSON.</summary>
        private static List<(string FieldName, bool FromPreviousStep)> ReadRules(string? conditionJson)
        {
            var rules = new List<(string, bool)>();
            if (string.IsNullOrWhiteSpace(conditionJson)) return rules;
            try
            {
                using var document = JsonDocument.Parse(conditionJson);
                if (document.RootElement.ValueKind != JsonValueKind.Object) return rules;
                if (!document.RootElement.TryGetProperty("conditions", out var conditions)
                    || conditions.ValueKind != JsonValueKind.Array) return rules;

                foreach (var rule in conditions.EnumerateArray())
                {
                    if (rule.ValueKind != JsonValueKind.Object) continue;
                    if (!rule.TryGetProperty("fieldName", out var name) || name.ValueKind != JsonValueKind.String) continue;
                    var fromPrevious = rule.TryGetProperty("fromPreviousStep", out var flag) && flag.ValueKind == JsonValueKind.True;
                    rules.Add((name.GetString()!, fromPrevious));
                }
            }
            catch (JsonException)
            {
                // Not JSON at all: nothing to judge.
            }
            return rules;
        }
    }
}
