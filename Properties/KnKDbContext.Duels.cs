using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Properties;

/// <summary>
/// Arena duel match records, the review policy and duel restrictions (arena duels DESIGN.md §4.3, §10.3;
/// KNG-213), in their own partial. Users, arenas and kits a match names can't be deleted while the match
/// exists (append-only history); a deleted battleground just clears the match's battleground.
/// </summary>
public partial class KnKDbContext
{
    public virtual DbSet<DuelMatch> DuelMatches { get; set; } = null!;
    public virtual DbSet<DuelPolicy> DuelPolicies { get; set; } = null!;
    public virtual DbSet<DuelRestriction> DuelRestrictions { get; set; } = null!;

    private static void ConfigureDuels(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DuelMatch>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("duel_matches", t =>
            {
                t.HasCheckConstraint("CK_duel_matches_Coins_NonNegative",
                    "`ChallengerCoins` >= 0 AND `OpponentCoins` >= 0 AND `FeeCoins` >= 0");
                t.HasCheckConstraint("CK_duel_matches_DistinctPlayers", "`ChallengerUserId` <> `OpponentUserId`");
            });

            entity.Property(e => e.PublicToken).IsRequired().HasMaxLength(36);
            entity.HasIndex(e => e.PublicToken).IsUnique();

            entity.Property(e => e.GearMode).HasConversion<string>().HasMaxLength(32);
            entity.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.EndReason).HasConversion<string>().HasMaxLength(30);
            entity.Property(e => e.Review).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.UndeliveredItems).HasConversion<string>().HasMaxLength(20);
            entity.Property(e => e.ReviewSignals).HasMaxLength(64);
            entity.Property(e => e.ReviewReason).HasMaxLength(500);
            entity.Property(e => e.StakeItemsJson).HasColumnType("longtext");
            entity.Property(e => e.SpoilsItemsJson).HasColumnType("longtext");
            entity.Property(e => e.FightTelemetryJson).HasColumnType("longtext");
            entity.Property(e => e.ReviewDetailsJson).HasColumnType("longtext");
            entity.Property(e => e.RevertResultJson).HasColumnType("longtext");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.LockedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.FightStartedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.EndedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.ReviewedAt).HasColumnType("datetime(6)");
            entity.Ignore(e => e.Pot);
            entity.Ignore(e => e.IsValueMoving);
            entity.Ignore(e => e.LoserUserId);

            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Review);
            entity.HasIndex(e => new { e.ChallengerUserId, e.CreatedAt });
            entity.HasIndex(e => new { e.OpponentUserId, e.CreatedAt });
            entity.HasIndex(e => new { e.WinnerUserId, e.EndedAt });
            entity.HasIndex(e => new { e.ArenaId, e.Status });
            entity.HasIndex(e => e.SettlementTransactionId);

            entity.HasOne(e => e.Arena).WithMany().HasForeignKey(e => e.ArenaId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.Battleground).WithMany().HasForeignKey(e => e.BattlegroundId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.ChallengerUser).WithMany().HasForeignKey(e => e.ChallengerUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(e => e.OpponentUser).WithMany().HasForeignKey(e => e.OpponentUserId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Kit>().WithMany().HasForeignKey(e => e.FixedKitId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<Kit>().WithMany().HasForeignKey(e => e.ChallengerKitId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<Kit>().WithMany().HasForeignKey(e => e.OpponentKitId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DuelPolicy>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("duel_policies", t =>
                t.HasCheckConstraint("CK_duel_policies_EscalationFlagCount_AtLeastTwo", "`EscalationFlagCount` >= 2"));
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.UpdatedAt).HasColumnType("datetime(6)");
            entity.HasData(new DuelPolicy
            {
                Id = DuelPolicy.SingletonId,
                UpdatedAt = new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        modelBuilder.Entity<DuelRestriction>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");
            entity.ToTable("duel_restrictions");
            entity.Property(e => e.Reason).IsRequired().HasMaxLength(500);
            entity.Property(e => e.LiftReason).HasMaxLength(500);
            entity.Property(e => e.ExpiresAt).HasColumnType("datetime(6)");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime(6)");
            entity.Property(e => e.LiftedAt).HasColumnType("datetime(6)");
            entity.HasIndex(e => new { e.UserId, e.LiftedAt });
            entity.HasIndex(e => new { e.OtherUserId, e.LiftedAt });
            entity.HasOne(e => e.User).WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.OtherUser).WithMany().HasForeignKey(e => e.OtherUserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
