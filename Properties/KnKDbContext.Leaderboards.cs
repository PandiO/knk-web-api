using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Properties;

/// <summary>
/// Leaderboard snapshot tables (KNG-34, knk-workspace docs/specs/player-statistics/
/// IMPLEMENTATION_PLAN.md §1.2). Configured from <see cref="ConfigureStatistics"/>'s partial hook.
/// No FK to users (like the statistics tables); entries belong to their snapshot (cascade).
/// </summary>
public partial class KnKDbContext
{
    public virtual DbSet<LeaderboardSnapshot> LeaderboardSnapshots { get; set; } = null!;
    public virtual DbSet<LeaderboardSnapshotEntry> LeaderboardSnapshotEntries { get; set; } = null!;

    private static void ConfigureLeaderboards(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LeaderboardSnapshot>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("leaderboard_snapshots");

            entity.Property(e => e.BoardKey).HasMaxLength(96);
            entity.Property(e => e.Period).HasConversion<byte>();
            entity.Property(e => e.PeriodStart).HasColumnType("date");
            entity.Property(e => e.GeneratedAt).HasColumnType("datetime(6)");

            entity.HasIndex(e => new { e.BoardKey, e.Period, e.IsCurrent });
        });

        modelBuilder.Entity<LeaderboardSnapshotEntry>(entity =>
        {
            entity.HasKey(e => new { e.SnapshotId, e.UserId }).HasName("PRIMARY");
            entity.ToTable("leaderboard_snapshot_entries");

            entity.Property(e => e.Value).HasPrecision(20, 4);
            entity.Property(e => e.ReachedAt).HasColumnType("datetime(6)");

            entity.HasIndex(e => new { e.SnapshotId, e.Rank });

            entity.HasOne(e => e.Snapshot)
                .WithMany(s => s.Entries)
                .HasForeignKey(e => e.SnapshotId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
