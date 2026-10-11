using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.WorldAnalytics;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Tests.Services.WorldAnalytics;

/// <summary>
/// EF InMemory database with the link-7 services wired by hand. "Now" is 2026-10-03 12:00 UTC
/// (14:00 in Europe/Amsterdam). Domains 7 (Aldmoor, region "aldmoor") and 8 (Brightwater, "brightwater").
/// </summary>
internal sealed class WorldAnalyticsTestDb : IDisposable
{
    public static readonly DateTime Now = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateOnly Today = new(2026, 10, 3);

    private readonly string _name = Guid.NewGuid().ToString();

    public WorldAnalyticsTestDb()
    {
        Context = NewContext();
        Context.Domains.AddRange(
            new Domain { Id = 7, Name = "Aldmoor", Description = "", WgRegionId = "aldmoor" },
            new Domain { Id = 8, Name = "Brightwater", Description = "", WgRegionId = "brightwater" });
        Context.SaveChanges();
    }

    public KnKDbContext Context { get; }

    public WorldAnalyticsOptions Options { get; } = new();

    public TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");

    public DateTime Clock { get; set; } = Now;

    public KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_name).Options);

    public WorldAnalyticsRepository Repository() => new(Context);

    public WorldAnalyticsIngestionService Ingestion() => new(Repository(), Options, Zone, () => Clock);

    public WorldAnalyticsQueryService Query() => new(Repository(), Options, Zone, () => Clock);

    public static WorldAnalyticsBatchDto Batch(DateTime? windowStart = null, Guid? id = null) => new()
    {
        BatchId = id ?? Guid.NewGuid(),
        ServerName = "paper-1",
        WindowStart = windowStart ?? Now.AddMinutes(-5),
        MovementCells = new(),
        MenuSteps = new(),
        DomainInteractions = new()
    };

    public static WorldMovementCellDto Cell(int x, int z, int samples, string world = "world", int size = 16) =>
        new() { World = world, CellSize = size, CellX = x, CellZ = z, Samples = samples };

    public static MenuFunnelStepDto Step(string menu, string step, int count, string? outcome = null) =>
        new() { MenuKey = menu, Step = step, Count = count, Outcome = outcome };

    public static DomainInteractionDto Domain(string kind, int count, int unique, int? domainId = null, string? regionId = null) =>
        new() { DomainId = domainId, RegionId = regionId, Kind = kind, Count = count, UniquePlayers = unique };

    public void Dispose() => Context.Dispose();
}
