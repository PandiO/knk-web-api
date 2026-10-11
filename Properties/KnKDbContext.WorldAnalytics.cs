using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Properties;

/// <summary>
/// World analytics tables (KNG-34 link 7, knk-workspace docs/specs/player-statistics/
/// IMPLEMENTATION_PLAN.md §1.4). Configured from <see cref="ConfigureStatistics"/>'s partial hook.
/// Anonymous daily aggregates: no user ids and no FKs (a deleted domain keeps its history).
/// </summary>
public partial class KnKDbContext
{
    public virtual DbSet<WorldMovementCellDaily> WorldMovementCellDailies { get; set; } = null!;
    public virtual DbSet<MenuFunnelDaily> MenuFunnelDailies { get; set; } = null!;
    public virtual DbSet<DomainInteractionDaily> DomainInteractionDailies { get; set; } = null!;
    public virtual DbSet<WorldAnalyticsBatch> WorldAnalyticsBatches { get; set; } = null!;

    private static void ConfigureWorldAnalytics(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorldMovementCellDaily>(entity =>
        {
            entity.HasKey(e => new { e.Day, e.World, e.CellSize, e.CellX, e.CellZ }).HasName("PRIMARY");
            entity.ToTable("world_movement_cells_daily");

            entity.Property(e => e.Day).HasColumnType("date");
            entity.Property(e => e.World).HasMaxLength(64);

            // Heatmap reads: one world over a day range.
            entity.HasIndex(e => new { e.World, e.Day }).HasDatabaseName("IX_world_movement_cells_daily_world_day");
        });

        modelBuilder.Entity<MenuFunnelDaily>(entity =>
        {
            entity.HasKey(e => new { e.Day, e.MenuKey, e.Step, e.Outcome }).HasName("PRIMARY");
            entity.ToTable("menu_funnel_daily");

            entity.Property(e => e.Day).HasColumnType("date");
            entity.Property(e => e.MenuKey).HasMaxLength(191);
            entity.Property(e => e.Step).HasMaxLength(96);
            entity.Property(e => e.Outcome).HasConversion<byte>();

            entity.HasIndex(e => new { e.MenuKey, e.Day }).HasDatabaseName("IX_menu_funnel_daily_menu_day");
        });

        modelBuilder.Entity<DomainInteractionDaily>(entity =>
        {
            entity.HasKey(e => new { e.Day, e.DomainId, e.Kind }).HasName("PRIMARY");
            entity.ToTable("domain_interactions_daily");

            entity.Property(e => e.Day).HasColumnType("date");
            entity.Property(e => e.Kind).HasMaxLength(32);

            entity.HasIndex(e => new { e.DomainId, e.Day }).HasDatabaseName("IX_domain_interactions_daily_domain_day");
        });

        modelBuilder.Entity<WorldAnalyticsBatch>(entity =>
        {
            entity.HasKey(e => e.BatchId).HasName("PRIMARY");
            entity.ToTable("world_analytics_batches");

            entity.Property(e => e.BatchId).HasColumnType("char(36)");
            entity.Property(e => e.ServerName).HasMaxLength(64);
            entity.Property(e => e.ReceivedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.WindowStart).HasColumnType("datetime(6)");

            entity.HasIndex(e => e.ReceivedAt);
        });
    }
}
