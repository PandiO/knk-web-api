using System;
using System.Threading.Tasks;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.ValidationResultLog
{
    /// <summary>KNG-269: storage of the most recent run per (rule, subject); see <see cref="FieldValidationRunLog"/>.</summary>
    public interface IValidationResultLog
    {
        Task<FieldValidationRunLog?> FindAsync(int ruleId, string subjectType, long subjectId);

        /// <summary>Inserts or replaces the row for (rule, subject). Never throws for a lost race: the other writer's row stands.</summary>
        Task StoreAsync(FieldValidationRunLog entry);

        /// <summary>Drops the rows of one subject and the rows whose dependency was that record (e.g. after its region changed). Returns how many.</summary>
        Task<int> InvalidateEntityAsync(string entityType, long id);

        /// <summary>Drops rows whose run is older than the cutoff. Returns how many.</summary>
        Task<int> PurgeOlderThanAsync(DateTime cutoffUtc);
    }

    /// <summary>What a validation run is about: the entity being edited, or the draft being filled before it exists.</summary>
    public sealed record ValidationSubject(string Type, long Id)
    {
        public const string DraftType = "FormSubmissionProgress";

        public static ValidationSubject? From(string? type, long? id) =>
            string.IsNullOrWhiteSpace(type) || id is null or <= 0 ? null : new ValidationSubject(type.Trim(), id.Value);
    }
}
