using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// PandiO/knk-workspace#269 on a real MySQL (migration <c>AddTempRegionReleases</c> applied by the fixture): the "something
/// names the region" checks run as SQL text searches, case-insensitively, so a draft or a lootbox area's exclusions that
/// name the region keep it, while an unused one is listed and settled.
/// </summary>
[Trait("Category", "requires-mysql")]
public class TempRegionReleaseMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;
    private static readonly DateTime Now = new(2026, 11, 9, 12, 0, 0, DateTimeKind.Utc);

    public TempRegionReleaseMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private TempRegionReleaseService Releases(knkwebapi_v2.Properties.KnKDbContext ctx) => new(ctx, Options.Create(new WorldTaskOptions()), utcNow: () => Now);

    [MySqlFact]
    public async Task NamedRegionsAreKept_AnUnusedOneIsListedAndSettled()
    {
        int usedByDraft, usedByArea, unused;
        await using (var ctx = _db.NewContext())
        {
            var user = new User { Username = "trr" + Guid.NewGuid().ToString("N")[..10] };
            var config = new FormConfiguration { Name = "Town " + Guid.NewGuid().ToString("N")[..8], EntityTypeName = "Town" };
            ctx.Users.Add(user);
            ctx.FormConfigurations.Add(config);
            await ctx.SaveChangesAsync();
            var session = new WorkflowSession { UserId = user.Id, EntityTypeName = "Town", CreatedAt = Now.AddHours(-2) };
            ctx.WorkflowSessions.Add(session);
            await ctx.SaveChangesAsync();
            WorldTask Task() => new()
            {
                WorkflowSessionId = session.Id, TaskType = "ReagionCreate", FieldName = "WgRegionId", StepKey = "s",
                Status = WorldTaskStatuses.Cancelled, ClaimedAt = Now.AddHours(-1), ClaimedByServerId = "survival-1",
                CreatedAt = Now.AddHours(-1), UpdatedAt = Now.AddHours(-1)
            };
            var a = Task();
            var b = Task();
            var c = Task();
            ctx.WorldTasks.AddRange(a, b, c);
            await ctx.SaveChangesAsync();
            (usedByDraft, usedByArea, unused) = (a.Id, b.Id, c.Id);

            ctx.FormSubmissionProgresses.Add(new FormSubmissionProgress
            {
                UserId = user.Id, FormConfigurationId = config.Id,
                CurrentStepDataJson = $"{{\"WgRegionId\":\"TEMPREGION_WORLDTASK_{usedByDraft}\"}}"
            });
            ctx.LootboxSpawnAreas.Add(new LootboxSpawnArea
            {
                Name = "trr" + Guid.NewGuid().ToString("N")[..8], World = "knk_hub", WgRegionId = "lootbox_trr",
                ExcludedRegionIds = $"spawn, TempRegion_WorldTask_{usedByArea}"
            });
            foreach (var task in new[] { a, b, c })
            {
                await TempRegionReleases.AddIfNewAsync(ctx, task, TempRegionReleaseReasons.TaskCancelled, Now.AddMinutes(-10));
            }
            await ctx.SaveChangesAsync();
        }

        List<TempRegionReleaseDto> due;
        await using (var ctx = _db.NewContext())
        {
            due = (await Releases(ctx).ListDueAsync("survival-1", 200))
                .Where(d => d.WorldTaskId == usedByDraft || d.WorldTaskId == usedByArea || d.WorldTaskId == unused).ToList();
        }

        var listed = Assert.Single(due);
        Assert.Equal(unused, listed.WorldTaskId);
        await using (var ctx = _db.NewContext())
        {
            Assert.True(await Releases(ctx).SettleAsync(listed.Id, new TempRegionReleaseSettleDto { Outcome = "Removed", ServerId = "survival-1" }));
        }
        await using (var ctx = _db.NewContext())
        {
            Assert.False(await ctx.TempRegionReleases.AnyAsync(r => r.WorldTaskId == unused));
            Assert.Equal(2, await ctx.TempRegionReleases.CountAsync(r => r.WorldTaskId == usedByDraft || r.WorldTaskId == usedByArea));
        }
    }
}
