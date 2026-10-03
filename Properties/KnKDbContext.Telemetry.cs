using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Properties;

/// <summary>
/// Diagnostic telemetry and GDPR request tables (KNG-34 link 6, knk-workspace
/// docs/specs/player-statistics/IMPLEMENTATION_PLAN.md §1.3). Configured from
/// <see cref="ConfigureStatistics"/>'s partial hook. No FKs to users: diagnostics never block user
/// maintenance, and the GDPR deletion removes the rows explicitly.
/// </summary>
public partial class KnKDbContext
{
    public virtual DbSet<TelemetryEvent> TelemetryEvents { get; set; } = null!;
    public virtual DbSet<TelemetryTestRun> TelemetryTestRuns { get; set; } = null!;
    public virtual DbSet<TelemetryEnhancedTarget> TelemetryEnhancedTargets { get; set; } = null!;
    public virtual DbSet<PrivacyDeletionRequest> PrivacyDeletionRequests { get; set; } = null!;

    private static void ConfigureTelemetry(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TelemetryEvent>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("telemetry_events");

            entity.Property(e => e.EventId).HasColumnType("char(36)");
            entity.Property(e => e.Name).HasMaxLength(64);
            entity.Property(e => e.Level).HasConversion<byte>();
            entity.Property(e => e.Source).HasConversion<byte>();
            entity.Property(e => e.OccurredAt).HasColumnType("datetime(6)");
            entity.Property(e => e.ReceivedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.ServerName).HasMaxLength(64);
            entity.Property(e => e.AppVersion).HasMaxLength(32);
            entity.Property(e => e.SessionKey).HasColumnType("char(36)");
            entity.Property(e => e.CorrelationId).HasMaxLength(64);
            entity.Property(e => e.Feature).HasMaxLength(32);
            entity.Property(e => e.Action).HasMaxLength(64);
            entity.Property(e => e.Outcome).HasConversion<byte>();
            entity.Property(e => e.ReasonCode).HasMaxLength(64);
            entity.Property(e => e.ObjectType).HasMaxLength(32);
            entity.Property(e => e.ObjectId).HasMaxLength(64);
            entity.Property(e => e.PayloadJson).HasColumnType("json");

            entity.HasIndex(e => e.EventId).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.OccurredAt });
            entity.HasIndex(e => e.SessionKey);
            entity.HasIndex(e => new { e.TestRunId, e.OccurredAt });
            entity.HasIndex(e => new { e.MatchId, e.OccurredAt });
            entity.HasIndex(e => e.CorrelationId);
            entity.HasIndex(e => new { e.Name, e.OccurredAt });
            entity.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<TelemetryTestRun>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("telemetry_test_runs");

            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.Description).HasMaxLength(500);
            entity.Property(e => e.StartedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.EndedAt).HasColumnType("datetime(6)");
        });

        modelBuilder.Entity<TelemetryEnhancedTarget>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("telemetry_enhanced_targets", t =>
                t.HasCheckConstraint("CK_telemetry_enhanced_targets_OneTarget",
                    "(`UserId` IS NULL) <> (`TestRunId` IS NULL)"));

            entity.Property(e => e.ExpiresAt).HasColumnType("datetime(6)");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime(6)");

            entity.HasIndex(e => e.ExpiresAt);
        });

        modelBuilder.Entity<PrivacyDeletionRequest>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("privacy_deletion_requests");

            entity.Property(e => e.RequestedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.DueAt).HasColumnType("datetime(6)");
            entity.Property(e => e.Status).HasConversion<byte>();
            entity.Property(e => e.Note).HasMaxLength(500);
            entity.Property(e => e.ExecutedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.ResultJson).HasColumnType("json");

            entity.HasIndex(e => new { e.Status, e.DueAt });
            entity.HasIndex(e => e.UserId);
        });
    }
}
