using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class FormSubmissionProgressRepository : IFormSubmissionProgressRepository
    {
        private readonly KnKDbContext _context;

        public FormSubmissionProgressRepository(KnKDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// When propertyName/propertyValue are both supplied, additionally filters to rows whose
        /// saved field data (CurrentStepDataJson, or any step inside the step-partitioned
        /// AllStepsDataJson) contains that property with a matching value. Used to find drafts
        /// "for" a specific parent entity - e.g. GateDoor drafts whose GateStructureId matches a
        /// given GateStructure - since there's no relational column for that link, only whatever
        /// the wizard already saved into the JSON blobs.
        ///
        /// Filtered in-memory (after the DB-level EntityTypeName/UserId filter) rather than via a
        /// SQL JSON path: CurrentStepDataJson/AllStepsDataJson are plain longtext columns (no JSON
        /// column type, no EF JSON-function usage anywhere in this codebase), and AllStepsDataJson
        /// is step-partitioned ({"Step0": {...}, "Step1": {...}}) with the step key holding a given
        /// field unknown ahead of time, so a fixed JSON path can't reliably express "search every
        /// step" the way this in-memory walk does.
        /// </summary>
        public async Task<IEnumerable<FormSubmissionProgress>> GetByEntityTypeNameAsync(
            string entityTypeName, int? userId, string? propertyName = null, string? propertyValue = null)
        {
            var query = _context.FormSubmissionProgresses
                .Include(p => p.FormConfiguration)
                .Include(p => p.ParentProgress)
                .Include(p => p.User)
                .Where(p => p.FormConfiguration.EntityTypeName == entityTypeName);

            if (userId.HasValue)
            {
                query = query.Where(p => p.UserId == userId.Value);
            }

            var results = await query.ToListAsync();

            if (!string.IsNullOrWhiteSpace(propertyName) && propertyValue != null)
            {
                results = results.Where(p => MatchesProperty(p, propertyName, propertyValue)).ToList();
            }

            return results;
        }

        private static bool MatchesProperty(FormSubmissionProgress progress, string propertyName, string propertyValue)
        {
            return JsonObjectContainsMatch(progress.CurrentStepDataJson, propertyName, propertyValue)
                || JsonStepsContainMatch(progress.AllStepsDataJson, propertyName, propertyValue);
        }

        private static bool JsonObjectContainsMatch(string? json, string propertyName, string propertyValue)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                return ObjectHasMatchingProperty(doc.RootElement, propertyName, propertyValue);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool JsonStepsContainMatch(string? json, string propertyName, string propertyValue)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    return false;
                }

                foreach (var step in doc.RootElement.EnumerateObject())
                {
                    if (ObjectHasMatchingProperty(step.Value, propertyName, propertyValue))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static bool ObjectHasMatchingProperty(JsonElement element, string propertyName, string propertyValue)
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return ValueMatches(property.Value, propertyValue);
                }
            }

            return false;
        }

        /// <summary>
        /// Matches a bare primitive value directly, or an object value via its own "id" property
        /// (the shape an Object-type field's value takes, e.g. { id: 14, name: "...", ... }).
        /// </summary>
        private static bool ValueMatches(JsonElement value, string propertyValue)
        {
            switch (value.ValueKind)
            {
                case JsonValueKind.String:
                    return string.Equals(value.GetString(), propertyValue, StringComparison.OrdinalIgnoreCase);
                case JsonValueKind.Number:
                    return value.ToString() == propertyValue;
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return string.Equals(value.ToString(), propertyValue, StringComparison.OrdinalIgnoreCase);
                case JsonValueKind.Object:
                    foreach (var nested in value.EnumerateObject())
                    {
                        if (string.Equals(nested.Name, "id", StringComparison.OrdinalIgnoreCase))
                        {
                            return ValueMatches(nested.Value, propertyValue);
                        }
                    }
                    return false;
                default:
                    return false;
            }
        }
        
        public async Task<IEnumerable<FormSubmissionProgress>> GetByUserIdAsync(int userId)
        {
            return await _context.FormSubmissionProgresses
                .Where(p => p.UserId == userId)
                .Include(p => p.FormConfiguration)
                .Include(p => p.ParentProgress)
                .ToListAsync();
        }

        public async Task<FormSubmissionProgress?> GetByIdAsync(int id)
        {
            return await _context.FormSubmissionProgresses
                .Include(p => p.FormConfiguration)
                    .ThenInclude(fc => fc.Steps)
                        .ThenInclude(s => s.Fields)
                .Include(p => p.ParentProgress)
                .FirstOrDefaultAsync(p => p.Id == id);
        }

        public async Task AddAsync(FormSubmissionProgress progress)
        {
            await _context.FormSubmissionProgresses.AddAsync(progress);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(FormSubmissionProgress progress)
        {
            progress.UpdatedAt = System.DateTime.UtcNow;
            _context.FormSubmissionProgresses.Update(progress);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var progress = await _context.FormSubmissionProgresses.FindAsync(id);
            if (progress != null)
            {
                _context.FormSubmissionProgresses.Remove(progress);
                await _context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Get all completed form submissions that are older than the specified date.
        /// Used for retention policy cleanup.
        /// </summary>
        public async Task<IEnumerable<FormSubmissionProgress>> GetCompletedOlderThanAsync(System.DateTime beforeDate)
        {
            return await _context.FormSubmissionProgresses
                .Where(p => p.Status == "Completed" && p.CompletedAt.HasValue && p.CompletedAt < beforeDate)
                .ToListAsync();
        }

        /// <summary>
        /// Delete all completed form submissions older than the specified date.
        /// Deletes the deepest descendants first so parent/child hierarchies remain valid
        /// even when ParentProgressId is configured with Restrict delete behavior.
        /// Returns the count of deleted records.
        /// </summary>
        public async Task<int> DeleteCompletedOlderThanAsync(System.DateTime beforeDate)
        {
            var expiredRootIds = await _context.FormSubmissionProgresses
                .AsNoTracking()
                .Where(p => p.Status == "Completed" && p.CompletedAt.HasValue && p.CompletedAt < beforeDate)
                .Select(p => p.Id)
                .ToListAsync();

            if (expiredRootIds.Count == 0)
            {
                return 0;
            }

            var staleBranchIds = new HashSet<int>(expiredRootIds);
            var queue = new Queue<int>(expiredRootIds);

            while (queue.Count > 0)
            {
                var currentParentId = queue.Dequeue();

                var childIds = await _context.FormSubmissionProgresses
                    .AsNoTracking()
                    .Where(p => p.ParentProgressId == currentParentId)
                    .Select(p => p.Id)
                    .ToListAsync();

                foreach (var childId in childIds)
                {
                    if (staleBranchIds.Add(childId))
                    {
                        queue.Enqueue(childId);
                    }
                }
            }

            var descendantsByParent = await _context.FormSubmissionProgresses
                .AsNoTracking()
                .Where(p => p.ParentProgressId.HasValue && staleBranchIds.Contains(p.ParentProgressId.Value))
                .Select(p => new { ParentId = p.ParentProgressId!.Value, ChildId = p.Id })
                .GroupBy(x => x.ParentId)
                .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.ChildId).ToHashSet());

            var deleteOrder = new List<int>();
            var remaining = new HashSet<int>(staleBranchIds);

            while (remaining.Count > 0)
            {
                var leafIds = remaining
                    .Where(id => !descendantsByParent.TryGetValue(id, out var childIds) || childIds.All(childId => !remaining.Contains(childId)))
                    .ToList();

                if (leafIds.Count == 0)
                {
                    leafIds = remaining.ToList();
                }

                foreach (var id in leafIds)
                {
                    remaining.Remove(id);
                    deleteOrder.Add(id);
                }
            }

            var rowsToDelete = await _context.FormSubmissionProgresses
                .Where(p => deleteOrder.Contains(p.Id))
                .ToListAsync();

            if (rowsToDelete.Count == 0)
            {
                return 0;
            }

            _context.FormSubmissionProgresses.RemoveRange(rowsToDelete);
            await _context.SaveChangesAsync();

            return rowsToDelete.Count;
        }
    }
}
