using System;
using System.Linq;
using System.Threading.Tasks;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace knkwebapi_v2.Services.ValidationResultLog
{
    /// <summary>EF Core implementation of <see cref="IValidationResultLog"/> over <c>FieldValidationRunLogs</c>.</summary>
    public class EfValidationResultLog : IValidationResultLog
    {
        private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(1);
        private static DateTime _lastPurgeUtc = DateTime.MinValue;
        private static readonly object PurgeGate = new();

        private readonly KnKDbContext _context;
        private readonly ValidationResultLogOptions _options;
        private readonly ILogger<EfValidationResultLog> _logger;

        public EfValidationResultLog(KnKDbContext context, IOptions<ValidationResultLogOptions> options, ILogger<EfValidationResultLog> logger)
        {
            _context = context;
            _options = options.Value;
            _logger = logger;
        }

        public Task<FieldValidationRunLog?> FindAsync(int ruleId, string subjectType, long subjectId) =>
            _context.FieldValidationRunLogs.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RuleId == ruleId && r.SubjectType == subjectType && r.SubjectId == subjectId);

        public async Task StoreAsync(FieldValidationRunLog entry)
        {
            try
            {
                var existing = await _context.FieldValidationRunLogs
                    .FirstOrDefaultAsync(r => r.RuleId == entry.RuleId && r.SubjectType == entry.SubjectType && r.SubjectId == entry.SubjectId);
                if (existing == null)
                {
                    _context.FieldValidationRunLogs.Add(entry);
                }
                else
                {
                    existing.RuleHash = entry.RuleHash;
                    existing.InputHash = entry.InputHash;
                    existing.DependencyKey = entry.DependencyKey;
                    existing.IsValid = entry.IsValid;
                    existing.IsBlocking = entry.IsBlocking;
                    existing.Message = entry.Message;
                    existing.PlaceholdersJson = entry.PlaceholdersJson;
                    existing.ExecutedAt = entry.ExecutedAt;
                }
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                // Two runs of the same rule for the same subject at once (a double click): the first row stands.
                _logger.LogDebug(ex, "Validation result for rule {RuleId} / {SubjectType} {SubjectId} not stored (concurrent write)", entry.RuleId, entry.SubjectType, entry.SubjectId);
                _context.ChangeTracker.Clear();
            }

            await PurgeIfDueAsync();
        }

        public async Task<int> InvalidateEntityAsync(string entityType, long id)
        {
            var key = $"{entityType}:{id}";
            var rows = await _context.FieldValidationRunLogs
                .Where(r => (r.SubjectType == entityType && r.SubjectId == id) || r.DependencyKey == key)
                .ToListAsync();
            if (rows.Count == 0) return 0;
            _context.FieldValidationRunLogs.RemoveRange(rows);
            await _context.SaveChangesAsync();
            return rows.Count;
        }

        public async Task<int> PurgeOlderThanAsync(DateTime cutoffUtc)
        {
            var rows = await _context.FieldValidationRunLogs.Where(r => r.ExecutedAt < cutoffUtc).ToListAsync();
            if (rows.Count == 0) return 0;
            _context.FieldValidationRunLogs.RemoveRange(rows);
            await _context.SaveChangesAsync();
            return rows.Count;
        }

        private async Task PurgeIfDueAsync()
        {
            if (_options.RetentionDays <= 0) return;
            lock (PurgeGate)
            {
                if (DateTime.UtcNow - _lastPurgeUtc < PurgeInterval) return;
                _lastPurgeUtc = DateTime.UtcNow;
            }
            try
            {
                var purged = await PurgeOlderThanAsync(DateTime.UtcNow.AddDays(-_options.RetentionDays));
                if (purged > 0) _logger.LogInformation("Purged {Count} validation results older than {Days} days", purged, _options.RetentionDays);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Validation result purge failed");
            }
        }
    }
}
