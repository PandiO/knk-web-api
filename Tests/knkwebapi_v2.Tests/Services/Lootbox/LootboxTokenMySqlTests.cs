using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Lootbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MySqlConnector;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Lootbox;

/// <summary>
/// Runs only when <c>KNK_TEST_MYSQL</c> holds a connection string; skipped otherwise (CI has no MySQL in the test job).
/// </summary>
public sealed class MySqlFactAttribute : FactAttribute
{
    public const string EnvironmentVariable = "KNK_TEST_MYSQL";

    public MySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EnvironmentVariable)))
            Skip = $"Set {EnvironmentVariable} to a MySQL 8 connection string for a scratch database (it is dropped and re-created).";
    }
}

/// <summary>
/// Lootboxes Phase 5 on a real MySQL 8 (the InMemory provider neither locks rows nor enforces unique indexes): a
/// duplicated token item opened at the same moment by several requests (the same player on two servers, and another
/// player holding a copy) produces exactly one claim and one ItemInstance; parallel retries of one click replay it;
/// the unique <c>lootbox_claims.LootboxTokenId</c> refuses a second claim row for a token outright.
/// <para>
/// Opt-in: <c>KNK_TEST_MYSQL="Server=127.0.0.1;Port=3306;[Database=knk_scratch;]User=…;Password=…;Allow User Variables=True;"</c>.
/// The database is dropped, every migration is applied, and the database is dropped again at the end.
/// </para>
/// </summary>
[Trait("Category", "requires-mysql")]
public class LootboxTokenMySqlTests
{
    /// <summary>
    /// The shared <c>KNK_TEST_MYSQL</c> convention (<c>MySql/MySqlTestDatabase</c>) is a server connection without
    /// <c>Database=</c>; this class then uses its own scratch database under <c>KNK_TEST_MYSQL_DB_PREFIX</c>.
    /// </summary>
    private static string ConnectionString
    {
        get
        {
            var builder = new MySqlConnectionStringBuilder(Environment.GetEnvironmentVariable(MySqlFactAttribute.EnvironmentVariable)!)
            {
                AllowUserVariables = true
            };
            if (string.IsNullOrEmpty(builder.Database))
                builder.Database = (Environment.GetEnvironmentVariable("KNK_TEST_MYSQL_DB_PREFIX") ?? "knk_test_") + "lootbox_tokens";
            return builder.ConnectionString;
        }
    }

    private static KnKDbContext NewContext()
    {
        var connection = ConnectionString;
        return new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseMySql(connection, ServerVersion.AutoDetect(connection))
            .Options);
    }

    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<LootboxMappingProfile>();
        cfg.AddProfile<ItemInstanceMappingProfile>();
        cfg.AddProfile<ItemBlueprintMappingProfile>();
        cfg.AddProfile<GradeMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
    }).CreateMapper();

    private static LootboxRuntimeService Runtime(KnKDbContext db)
    {
        var users = new UserRepository(db);
        var random = new CryptoLootRandom();
        return new LootboxRuntimeService(
            new LootboxRuntimeRepository(db),
            new LootboxTypeService(new LootboxTypeRepository(db), Mapper()),
            new ItemInstanceService(new ItemInstanceRepository(db), Mapper()),
            users,
            new AuditLogService(new AuditLogRepository(db), users),
            new LootboxRollEngine(random),
            random,
            TimeProvider.System,
            NullLogger<LootboxRuntimeService>.Instance);
    }

    private sealed record Seeded(int Alice, int Bob, int Weapons);

    private static async Task<Seeded> FreshDatabaseAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();

        var grade = new Grade { Name = "Rare", Stars = 3, DropChance = 40m };
        var category = new Category { Name = "Weapons" };
        var sword = new ItemBlueprint { Name = "Steel Sword", DefaultDisplayName = "&bSteel Sword", MaxStackSize = 1, Category = category, Grade = grade };
        var weapons = new LootboxType { Name = "Weapons Lootbox", Category = category, MinBoxStars = 3, MaxBoxStars = 3 };
        var alice = new User { Username = "alice", Email = "alice@example.test" };
        var bob = new User { Username = "bob", Email = "bob@example.test" };
        db.AddRange(grade, category, sword, weapons, alice, bob);
        db.LootboxConfigurations.Add(new LootboxConfiguration { Id = "global", MaxClaimsPerPlayerPerDay = 100 });
        await db.SaveChangesAsync();
        return new Seeded(alice.Id, bob.Id, weapons.Id);
    }

    private static async Task DropAsync()
    {
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    private static async Task<Guid> IssueAsync(Seeded seeded)
    {
        await using var db = NewContext();
        var issued = await Runtime(db).IssueTokensAsync(
            new LootboxTokenIssueRequestDto { UserId = seeded.Alice, TypeId = seeded.Weapons, BoxStars = 3 }, null);
        return issued.Tokens.Single().Token;
    }

    // Each attempt is its own request: its own DbContext and connection, started together.
    private static async Task<(LootboxClaimResultDto? Result, string? Code)> AttemptAsync(Guid token, int userId, string key, Barrier start)
    {
        await using var db = NewContext();
        var runtime = Runtime(db);
        await db.Database.OpenConnectionAsync();
        start.SignalAndWait(TimeSpan.FromSeconds(30));
        try
        {
            return (await runtime.RedeemTokenAsync(token, new LootboxTokenRedeemRequestDto { UserId = userId, IdempotencyKey = key }), null);
        }
        catch (LootboxConflictException ex)
        {
            return (null, ex.Code);
        }
    }

    [MySqlFact]
    public async Task ConcurrentOpensOfADuplicatedToken_ProduceExactlyOneClaim()
    {
        var seeded = await FreshDatabaseAsync();
        try
        {
            for (var round = 0; round < 5; round++)
            {
                var token = await IssueAsync(seeded);
                // Three copies in Alice's hands (e.g. two servers), two in Bob's: five different clicks at once.
                var openers = new[] { seeded.Alice, seeded.Alice, seeded.Alice, seeded.Bob, seeded.Bob };
                using var start = new Barrier(openers.Length);
                var attempts = await Task.WhenAll(openers.Select((user, i) =>
                    Task.Run(() => AttemptAsync(token, user, $"open:{token:N}:{i}", start))));

                attempts.Count(a => a.Result != null).Should().Be(1, $"round {round}: one copy opens");
                attempts.Where(a => a.Result == null).Select(a => a.Code).Should().OnlyContain(c => c == "AlreadyRedeemed");

                await using var db = NewContext();
                var row = await db.LootboxTokens.SingleAsync(t => t.Token == token);
                row.Status.Should().Be(LootboxTokenStatus.Redeemed);
                var claims = await db.LootboxClaims.Where(c => c.LootboxTokenId == row.Id).ToListAsync();
                claims.Should().ContainSingle();
                claims[0].UserId.Should().Be(row.RedeemedByUserId!.Value);
                (await db.ItemInstances.CountAsync(i => i.OriginRef == claims[0].Id.ToString())).Should().Be(1);
            }
        }
        finally
        {
            await DropAsync();
        }
    }

    [MySqlFact]
    public async Task ParallelRetriesOfOneClick_ReplayTheSameClaim_AndTheUniqueIndexRefusesASecondClaimRow()
    {
        var seeded = await FreshDatabaseAsync();
        try
        {
            var token = await IssueAsync(seeded);
            using var start = new Barrier(4);
            var attempts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
                Task.Run(() => AttemptAsync(token, seeded.Alice, "open:same-click", start))));

            attempts.Should().OnlyContain(a => a.Result != null);
            attempts.Select(a => a.Result!.ClaimId).Distinct().Should().ContainSingle();
            attempts.Count(a => !a.Result!.Replay).Should().Be(1);

            await using var db = NewContext();
            var claim = await db.LootboxClaims.SingleAsync();
            db.LootboxClaims.Add(new LootboxClaim
            {
                LootboxTokenId = claim.LootboxTokenId,
                UserId = claim.UserId,
                LootboxTypeId = claim.LootboxTypeId,
                BoxGradeId = claim.BoxGradeId,
                ItemBlueprintId = claim.ItemBlueprintId,
                IdempotencyKey = "forged-second-claim",
                ClaimedAt = DateTime.UtcNow,
            });
            await db.Invoking(d => d.SaveChangesAsync()).Should().ThrowAsync<DbUpdateException>();
        }
        finally
        {
            await DropAsync();
        }
    }

    // A world box picked up (DESIGN.md §3.8): its own DbContext and connection per click, started together.
    private static async Task<(LootboxPickupResultDto? Result, string? Code)> PickupAttemptAsync(int spawnId, Guid token, int userId, Barrier start)
    {
        await using var db = NewContext();
        var runtime = Runtime(db);
        await db.Database.OpenConnectionAsync();
        start.SignalAndWait(TimeSpan.FromSeconds(30));
        try
        {
            return (await runtime.PickupAsync(spawnId, new LootboxPickupRequestDto { Token = token, UserId = userId }), null);
        }
        catch (LootboxConflictException ex)
        {
            return (null, ex.Code);
        }
    }

    [MySqlFact]
    public async Task ConcurrentPickupsOfOneWorldBox_GiveExactlyOneToken()
    {
        var seeded = await FreshDatabaseAsync();
        try
        {
            for (var round = 0; round < 5; round++)
            {
                LootboxSpawnDto spawn;
                await using (var db = NewContext())
                {
                    spawn = await Runtime(db).AdminSpawnAsync(new LootboxAdminSpawnRequestDto
                    {
                        TypeId = seeded.Weapons, BoxStars = 3, World = "world", X = round, Y = 64, Z = 0,
                    }, null);
                }
                // Alice twice (a double click, two servers) and Bob three times, all at once.
                var pickers = new[] { seeded.Alice, seeded.Alice, seeded.Bob, seeded.Bob, seeded.Bob };
                using var start = new Barrier(pickers.Length);
                var attempts = await Task.WhenAll(pickers.Select(user =>
                    Task.Run(() => PickupAttemptAsync(spawn.Id, spawn.Token, user, start))));

                var winners = attempts.Where(a => a.Result != null).Select(a => a.Result!.LootboxToken.Token).Distinct().ToList();
                winners.Should().ContainSingle($"round {round}: one token for the box (a same-player retry may replay it)");
                attempts.Where(a => a.Result == null).Select(a => a.Code).Should().OnlyContain(c => c == "AlreadyClaimed");

                await using var read = NewContext();
                var tokens = await read.LootboxTokens.Where(t => t.SourceSpawnId == spawn.Id).ToListAsync();
                tokens.Should().ContainSingle();
                var box = await read.LootboxSpawns.SingleAsync(s => s.Id == spawn.Id);
                (box.Status, box.ClaimedByUserId).Should().Be((LootboxSpawnStatus.Claimed, tokens[0].IssuedToUserId));
                attempts.Where(a => a.Result != null).Should().OnlyContain(a => a.Result!.LootboxToken.IssuedToUserId == tokens[0].IssuedToUserId);
            }
        }
        finally
        {
            await DropAsync();
        }
    }
}
