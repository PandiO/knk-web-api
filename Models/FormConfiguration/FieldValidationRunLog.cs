using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// KNG-269: the most recent run of one <see cref="FieldValidationRule"/> for one subject (the entity
/// being edited, or the draft being filled before the entity exists). <c>ValidationService</c> answers
/// from this row instead of running the rule again while <see cref="RuleHash"/> and
/// <see cref="InputHash"/> still match (and, for a rule that reads external state, the row is not older
/// than the configured TTL). One row per (rule, subject): a real run replaces it.
/// </summary>
public class FieldValidationRunLog
{
    public int Id { get; set; }

    public int RuleId { get; set; }
    public FieldValidationRule Rule { get; set; } = null!;

    /// <summary>SHA-256 over the rule's behaviour (type, config, dependency, messages, blocking); see ValidationInputHasher.</summary>
    public string RuleHash { get; set; } = string.Empty;

    /// <summary>The entity type name in edit mode (e.g. "District"), or "FormSubmissionProgress" for a draft.</summary>
    public string SubjectType { get; set; } = string.Empty;
    public long SubjectId { get; set; }

    /// <summary>SHA-256 over the canonical JSON of the field value and the dependency values the validator reads.</summary>
    public string InputHash { get; set; } = string.Empty;

    /// <summary>"Town:3" when the dependency was a saved record, so a change to that record can drop the rows that read it.</summary>
    public string? DependencyKey { get; set; }

    public bool IsValid { get; set; }
    public bool IsBlocking { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PlaceholdersJson { get; set; }

    /// <summary>When the real run completed (UTC).</summary>
    public DateTime ExecutedAt { get; set; }
}
