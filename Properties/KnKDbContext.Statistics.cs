using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Properties;

/// <summary>
/// Player statistics tables (KNG-34, knk-workspace docs/specs/player-statistics/
/// IMPLEMENTATION_PLAN.md §1.1), kept in their own partial so the feature stays out of the main
/// OnModelCreating. No FKs to users, like the ledger: statistics never block user maintenance,
/// and the GDPR deletion (link 6) removes them explicitly.
/// </summary>
public partial class KnKDbContext
{
    public virtual DbSet<PlayerStatDaily> PlayerStatDailies { get; set; } = null!;
    public virtual DbSet<PlayerStatTotal> PlayerStatTotals { get; set; } = null!;
    public virtual DbSet<PlayerStatSession> PlayerStatSessions { get; set; } = null!;
    public virtual DbSet<PlayerStatBatch> PlayerStatBatches { get; set; } = null!;
    public virtual DbSet<PlayerStatVisibility> PlayerStatVisibilities { get; set; } = null!;
    public virtual DbSet<PlayerStatProfile> PlayerStatProfiles { get; set; } = null!;
    public virtual DbSet<PlayerTitleChange> PlayerTitleChanges { get; set; } = null!;
    public virtual DbSet<PlayerPvpKillPairDaily> PlayerPvpKillPairDailies { get; set; } = null!;
    public virtual DbSet<StatisticsProjectionCursor> StatisticsProjectionCursors { get; set; } = null!;
    public virtual DbSet<StatisticsProjectedSource> StatisticsProjectedSources { get; set; } = null!;

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder)
    {
        ConfigureStatistics(modelBuilder);
        ConfigureLeaderboards(modelBuilder);
        ConfigureTelemetry(modelBuilder);
    }

    private static void ConfigureStatistics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerStatDaily>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.Day, e.MetricKey, e.ContextKey }).HasName("PRIMARY");
            entity.ToTable("player_stat_daily");

            entity.Property(e => e.Day).HasColumnType("date");
            entity.Property(e => e.MetricKey).HasMaxLength(48);
            entity.Property(e => e.ContextKey).HasMaxLength(32);
            entity.Property(e => e.Value).HasPrecision(20, 4);
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime(6)");

            // Period leaderboards (link 5) and retention by day.
            entity.HasIndex(e => new { e.MetricKey, e.ContextKey, e.Day }).HasDatabaseName("IX_player_stat_daily_metric_day");
        });

        modelBuilder.Entity<PlayerStatTotal>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.MetricKey, e.ContextKey }).HasName("PRIMARY");
            entity.ToTable("player_stat_totals");

            entity.Property(e => e.MetricKey).HasMaxLength(48);
            entity.Property(e => e.ContextKey).HasMaxLength(32);
            entity.Property(e => e.Value).HasPrecision(20, 4);
            entity.Property(e => e.ReachedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime(6)");

            entity.HasIndex(e => new { e.MetricKey, e.ContextKey, e.Value });
        });

        modelBuilder.Entity<PlayerStatSession>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("player_stat_sessions");

            entity.Property(e => e.SessionKey).HasColumnType("char(36)");
            entity.Property(e => e.StartedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.LastHeartbeatAt).HasColumnType("datetime(6)");
            entity.Property(e => e.EndedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.EndReason).HasConversion<byte>();
            entity.Property(e => e.ServerName).HasMaxLength(64);

            entity.HasIndex(e => e.SessionKey).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.StartedAt });
            // Timeout sweep: open sessions by last heartbeat.
            entity.HasIndex(e => new { e.EndedAt, e.LastHeartbeatAt });
        });

        modelBuilder.Entity<PlayerStatBatch>(entity =>
        {
            entity.HasKey(e => e.BatchId).HasName("PRIMARY");
            entity.ToTable("player_stat_batches");

            entity.Property(e => e.BatchId).HasColumnType("char(36)");
            entity.Property(e => e.ServerName).HasMaxLength(64);
            entity.Property(e => e.ReceivedAt).HasColumnType("datetime(6)");

            entity.HasIndex(e => e.ReceivedAt);
        });

        modelBuilder.Entity<PlayerStatVisibility>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.SettingKey, e.ContextKey }).HasName("PRIMARY");
            entity.ToTable("player_stat_visibility");

            entity.Property(e => e.SettingKey).HasMaxLength(48);
            entity.Property(e => e.ContextKey).HasMaxLength(32);
            entity.Property(e => e.Visibility).HasConversion<byte>();
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime(6)");

            // Leaderboard eligibility (link 5).
            entity.HasIndex(e => new { e.SettingKey, e.ContextKey, e.Visibility });
        });

        modelBuilder.Entity<PlayerStatProfile>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PRIMARY");
            entity.ToTable("player_stat_profiles");

            entity.Property(e => e.UserId).ValueGeneratedNever();
            entity.Property(e => e.FirstSessionAt).HasColumnType("datetime(6)");
            entity.Property(e => e.LeaderboardExcludedReason).HasMaxLength(200);
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime(6)");
        });

        modelBuilder.Entity<PlayerTitleChange>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("player_title_changes");

            entity.Property(e => e.FromTitleName).HasMaxLength(64);
            entity.Property(e => e.ToTitleName).IsRequired().HasMaxLength(64);
            entity.Property(e => e.Direction).HasConversion<byte>();
            entity.Property(e => e.ChangedAt).HasColumnType("datetime(6)");

            entity.HasIndex(e => e.CurrencyEntryId).IsUnique();
            entity.HasIndex(e => new { e.UserId, e.ChangedAt });
        });

        modelBuilder.Entity<PlayerPvpKillPairDaily>(entity =>
        {
            entity.HasKey(e => new { e.KillerUserId, e.VictimUserId, e.Day, e.ContextKey }).HasName("PRIMARY");
            entity.ToTable("player_pvp_kill_pairs_daily");

            entity.Property(e => e.Day).HasColumnType("date");
            entity.Property(e => e.ContextKey).HasMaxLength(32);

            entity.HasIndex(e => e.Day);
        });

        modelBuilder.Entity<StatisticsProjectionCursor>(entity =>
        {
            entity.HasKey(e => e.Name).HasName("PRIMARY");
            entity.ToTable("statistics_projection_cursors");

            entity.Property(e => e.Name).HasMaxLength(48);
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime(6)");
        });

        modelBuilder.Entity<StatisticsProjectedSource>(entity =>
        {
            entity.HasKey(e => new { e.SourceType, e.SourceId }).HasName("PRIMARY");
            entity.ToTable("statistics_projected_sources");

            entity.Property(e => e.SourceType).HasMaxLength(32);
            entity.Property(e => e.ProjectedAt).HasColumnType("datetime(6)");
        });
    }
}
