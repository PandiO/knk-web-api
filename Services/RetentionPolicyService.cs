using System;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Background service that enforces retention policies.
    /// - Deletes completed FormSubmissionProgress records older than 14 days.
    /// - Deletes AuditLogEntry records older than the configurable retention window
    ///   (AuditLogRetentionConfiguration, docs/specs/user-management/DESIGN.md §7 item 3 —
    ///   default 180 days, re-read each run so a config change takes effect without a restart).
    /// - Deletes PrivateMessageLogEntry records older than
    ///   AuditLogRetentionConfiguration.PrivateMessageRetentionDays (docs/specs/private-messages/
    ///   DESIGN.md §3.1 — default 30 days, also re-read each run).
    /// Runs once per day at startup and then every 24 hours.
    /// <para>
    /// Never touches the currency ledger (currency_transactions / currency_entries): it is kept
    /// forever (docs/specs/currency-payments/DESIGN.md §3.1 invariant 6, §5 resolved item 11) and
    /// its tables refuse deletes by trigger. Don't add a ledger cleanup here or anywhere else —
    /// RetentionPolicyLedgerTests fails if this service ever deletes ledger rows.
    /// </para>
    /// </summary>
    public class RetentionPolicyService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RetentionPolicyService> _logger;
        private readonly int _retentionDays;
        private readonly TimeSpan _runInterval;

        /// <summary>Unfinished form submissions (drafts) are kept this long after their last update (KNG-43).</summary>
        public const int UnfinishedFormSubmissionRetentionDays = 90;

        public RetentionPolicyService(
            IServiceProvider serviceProvider,
            ILogger<RetentionPolicyService> logger,
            int retentionDays = 14,
            TimeSpan? runInterval = null)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _retentionDays = retentionDays;
            _runInterval = runInterval ?? TimeSpan.FromHours(24); // Default: run daily
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "RetentionPolicyService started. Will clean up records older than {RetentionDays} days every {Interval}",
                _retentionDays,
                _runInterval.TotalHours);

            // Run immediately on startup
            await RunCleanupAsync(stoppingToken);

            // Schedule recurring cleanup
            using (var timer = new PeriodicTimer(_runInterval))
            {
                try
                {
                    while (await timer.WaitForNextTickAsync(stoppingToken))
                    {
                        await RunCleanupAsync(stoppingToken);
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    _logger.LogInformation("RetentionPolicyService stopping");
                }
            }
        }

        private async Task RunCleanupAsync(CancellationToken cancellationToken)
        {
            // Each cleanup gets its own scope (DbContext) and try/catch, so a failing one (e.g.
            // the form-submission delete) never skips or poisons the others - the private message
            // cleanup in particular is a privacy promise (30 days), not housekeeping.
            await RunInScopeAsync(RunFormSubmissionCleanupAsync);
            await RunInScopeAsync(RunUnfinishedFormSubmissionCleanupAsync);
            await RunInScopeAsync(RunAuditLogCleanupAsync);
            await RunInScopeAsync(RunPrivateMessageLogCleanupAsync);
            await RunInScopeAsync(RunRefreshTokenCleanupAsync);
        }

        /// <summary>Refresh tokens are kept this long after they expire or are revoked (closed-alpha WP4),
        /// so a reuse attempt shortly after still finds the revoked row.</summary>
        public const int RefreshTokenRetentionDays = 7;

        private async Task RunRefreshTokenCleanupAsync(IServiceProvider scopedProvider)
        {
            var cutoff = DateTime.UtcNow.AddDays(-RefreshTokenRetentionDays);
            var repository = scopedProvider.GetRequiredService<IRefreshTokenRepository>();
            var deleted = await repository.DeleteExpiredOrRevokedBeforeAsync(cutoff);
            _logger.LogInformation("Refresh token cleanup completed. Deleted {Count} tokens expired or revoked before {CutoffDate}", deleted, cutoff);
        }

        private async Task RunInScopeAsync(Func<IServiceProvider, Task> cleanup)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                await cleanup(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running retention policy cleanup");
            }
        }

        private async Task RunFormSubmissionCleanupAsync(IServiceProvider scopedProvider)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-_retentionDays);
                
                _logger.LogInformation(
                    "Running retention policy cleanup. Deleting FormSubmissionProgress records completed before {CutoffDate}",
                    cutoffDate);

                var repository = scopedProvider.GetRequiredService<IFormSubmissionProgressRepository>();
                int deletedCount = await repository.DeleteCompletedOlderThanAsync(cutoffDate);

                _logger.LogInformation(
                    "Retention policy cleanup completed. Deleted {Count} completed form submissions",
                    deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running retention policy cleanup");
            }
        }

        /// <summary>
        /// Second, longer pass for drafts that were never finished (InProgress/Paused/Abandoned): deleted once they have
        /// not been touched for <see cref="UnfinishedFormSubmissionRetentionDays"/> days. Own try/catch so it can't affect
        /// the completed-submission, audit-log or private-message cleanups.
        /// </summary>
        private async Task RunUnfinishedFormSubmissionCleanupAsync(IServiceProvider scopedProvider)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-UnfinishedFormSubmissionRetentionDays);

                _logger.LogInformation(
                    "Running retention policy cleanup. Deleting unfinished FormSubmissionProgress records not updated since {CutoffDate}",
                    cutoffDate);

                var repository = scopedProvider.GetRequiredService<IFormSubmissionProgressRepository>();
                int deletedCount = await repository.DeleteUnfinishedOlderThanAsync(cutoffDate);

                _logger.LogInformation(
                    "Retention policy cleanup completed. Deleted {Count} unfinished form submissions",
                    deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running unfinished form submission retention cleanup");
            }
        }

        /// <summary>
        /// Separate try/catch from the FormSubmissionProgress cleanup above so a failure in one
        /// (e.g. the retention config row being unreadable) doesn't prevent the other from running.
        /// </summary>
        private async Task RunAuditLogCleanupAsync(IServiceProvider scopedProvider)
        {
            try
            {
                var retentionConfigService = scopedProvider.GetRequiredService<IAuditLogRetentionConfigurationService>();
                var retentionConfig = await retentionConfigService.GetAsync();
                var auditCutoffDate = DateTime.UtcNow.AddDays(-retentionConfig.RetentionDays);

                _logger.LogInformation(
                    "Running audit log retention cleanup. Deleting AuditLogEntry records older than {CutoffDate} ({RetentionDays}-day retention)",
                    auditCutoffDate,
                    retentionConfig.RetentionDays);

                var auditLogRepository = scopedProvider.GetRequiredService<IAuditLogRepository>();
                int deletedAuditCount = await auditLogRepository.DeleteOlderThanAsync(auditCutoffDate);

                _logger.LogInformation(
                    "Audit log retention cleanup completed. Deleted {Count} audit log entries",
                    deletedAuditCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running audit log retention cleanup");
            }
        }

        /// <summary>
        /// Private message content (KNG-18 Phase 3). Its own try/catch, like the audit cleanup, so
        /// one failing block never stops the others.
        /// </summary>
        private async Task RunPrivateMessageLogCleanupAsync(IServiceProvider scopedProvider)
        {
            try
            {
                var retentionConfigService = scopedProvider.GetRequiredService<IAuditLogRetentionConfigurationService>();
                var retentionConfig = await retentionConfigService.GetAsync();
                var cutoffDate = DateTime.UtcNow.AddDays(-retentionConfig.PrivateMessageRetentionDays);

                _logger.LogInformation(
                    "Running private message log retention cleanup. Deleting PrivateMessageLogEntry records sent before {CutoffDate} ({RetentionDays}-day retention)",
                    cutoffDate,
                    retentionConfig.PrivateMessageRetentionDays);

                var repository = scopedProvider.GetRequiredService<IPrivateMessageLogRepository>();
                int deletedCount = await repository.DeleteOlderThanAsync(cutoffDate);

                _logger.LogInformation(
                    "Private message log retention cleanup completed. Deleted {Count} private messages",
                    deletedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error running private message log retention cleanup");
            }
        }
    }
}
