using System.Diagnostics;
using System.Net;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.CloudflareAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace knkwebapi_v2.Tests.Services.CloudflareAccess;

/// <summary>
/// KNG-216 (IMPLEMENTATION_PLAN.md § 7.1): the Zero Trust email list client against a fake
/// api.cloudflare.com, the diff, the desired-set query, reconcile runs, the inline add with its
/// timeout, and "sync off means no calls".
/// </summary>
public class CloudflareAccessSyncTests
{
    private static (FakeCloudflare Cf, ServiceProvider Provider, FixedDesiredSet Desired) Build(CloudflareAccessSettings? settings = null)
    {
        var cf = new FakeCloudflare();
        var desired = new FixedDesiredSet();
        var provider = cf.Provider(settings ?? FakeCloudflare.Settings(), s => s.AddScoped<ICloudflareAccessDesiredSet>(_ => desired));
        return (cf, provider, desired);
    }

    // ------------------------------------------------------------ the list client

    [Fact]
    public async Task List_ReadsEveryPage_WithTheBearerToken_AndTheAccountListPath()
    {
        var (cf, provider, _) = Build();
        cf.Values.AddRange(Enumerable.Range(0, 2500).Select(i => $"p{i}@example.com"));
        var list = provider.GetRequiredService<ICloudflareAccessList>();

        var values = await list.GetEmailsAsync();

        Assert.Equal(2500, values.Count);
        Assert.Equal(3, cf.Requests.Count);
        Assert.Equal(ZeroTrustEmailList.PageSize, cf.PerPageSeen);
        Assert.All(cf.Requests, r =>
        {
            Assert.Equal(HttpMethod.Get, r.Method);
            Assert.StartsWith("/client/v4/accounts/acc123/gateway/lists/list-456/items?page=", r.PathAndQuery);
            Assert.Equal("Bearer " + FakeCloudflare.Token, r.Auth);
        });
    }

    [Fact]
    public async Task List_AppendsAndRemoves_WithOnePatchShapeEach()
    {
        var (cf, provider, _) = Build();
        cf.Values.Add("old@example.com");
        var list = provider.GetRequiredService<ICloudflareAccessList>();

        await list.AddAsync(new[] { "new@example.com" });
        await list.RemoveAsync(new[] { "old@example.com" });

        Assert.Equal(new[] { "new@example.com" }, cf.Values);
        Assert.Equal("{\"append\":[{\"value\":\"new@example.com\",\"description\":\"knk-web-api\"}]}", cf.Requests[0].Body);
        Assert.Equal("{\"remove\":[\"old@example.com\"]}", cf.Requests[1].Body);
        Assert.All(cf.Requests, r => Assert.Equal(HttpMethod.Patch, r.Method));
    }

    [Fact]
    public async Task List_Error_IsACloudflareApiException_WithoutTheToken()
    {
        var (cf, provider, _) = Build();
        cf.FailWith = HttpStatusCode.Forbidden;
        var list = provider.GetRequiredService<ICloudflareAccessList>();

        var ex = await Assert.ThrowsAsync<CloudflareApiException>(() => list.GetEmailsAsync());

        Assert.Contains("HTTP 403", ex.Message);
        Assert.Contains("10000: Authentication error", ex.Message);
        Assert.DoesNotContain(FakeCloudflare.Token, ex.Message);
    }

    [Fact]
    public void Settings_ReportWhatsMissing_OnlyWhenEnabled()
    {
        Assert.Null(new CloudflareAccessSettings().ConfigurationProblem());
        Assert.Contains("AccountId", new CloudflareAccessSettings { SyncEnabled = true }.ConfigurationProblem());
        Assert.Contains("ApiToken", new CloudflareAccessSettings { SyncEnabled = true, AccountId = "a", ListId = "l" }.ConfigurationProblem());
        Assert.Contains("ProxyUrl", new CloudflareAccessSettings { SyncEnabled = true, AccountId = "a", ListId = "l", ApiToken = "t", ProxyUrl = "cfproxy" }.ConfigurationProblem());
        Assert.Null(FakeCloudflare.Settings().ConfigurationProblem());
        Assert.False(new CloudflareAccessSettings().SyncEnabled); // behaviour-neutral default
    }

    // ------------------------------------------------------------ diff

    [Fact]
    public void Diff_IsCaseInsensitive_AndRemovesListValuesAsStored()
    {
        var (toAdd, toRemove) = CloudflareAccessSync.Diff(
            new[] { "a@example.com", " B@Example.com ", "c@example.com" },
            new[] { "A@Example.com", "b@example.com", "gone@example.com", "Gone@Example.com" });

        Assert.Equal(new[] { "c@example.com" }, toAdd);
        Assert.Equal(new[] { "gone@example.com", "Gone@Example.com" }, toRemove);
    }

    // ------------------------------------------------------------ reconcile

    [Fact]
    public async Task Reconcile_AddsMissing_RemovesExtra_AndRecordsSuccess()
    {
        var (cf, provider, desired) = Build();
        cf.Values.AddRange(new[] { "keep@example.com", "revoked@example.com" });
        desired.Emails = new() { "keep@example.com", "new@example.com" };
        var sync = provider.GetRequiredService<CloudflareAccessSync>();

        var result = await sync.ReconcileAsync();

        Assert.Equal(new CloudflareAccessRunResult(1, 1, 2, false), result);
        Assert.Equal(new[] { "keep@example.com", "new@example.com" }, cf.Values.OrderBy(v => v));
        var status = sync.GetStatus();
        Assert.True(status.Enabled);
        Assert.Null(status.LastError);
        Assert.NotNull(status.LastSuccessAt);
        Assert.Equal(0, status.PendingAdds);
        Assert.Equal(2, status.ListSize);
    }

    [Fact]
    public async Task Reconcile_InSync_OnlyReads()
    {
        var (cf, provider, desired) = Build();
        cf.Values.Add("a@example.com");
        desired.Emails = new() { "a@example.com" };

        await provider.GetRequiredService<CloudflareAccessSync>().ReconcileAsync();

        Assert.Single(cf.Requests);
        Assert.Equal(HttpMethod.Get, cf.Requests[0].Method);
    }

    [Fact]
    public async Task Reconcile_TooManyRemovals_HoldsThemBack_ButStillAdds()
    {
        var settings = FakeCloudflare.Settings();
        settings.MaxRemovalsPerRun = 2;
        var (cf, provider, desired) = Build(settings);
        cf.Values.AddRange(new[] { "x1@example.com", "x2@example.com", "x3@example.com" });
        desired.Emails = new() { "new@example.com" };
        var sync = provider.GetRequiredService<CloudflareAccessSync>();

        var result = await sync.ReconcileAsync();

        Assert.True(result.RemovalsSkipped);
        Assert.Equal(4, cf.Values.Count);
        var status = sync.GetStatus();
        Assert.Contains("3 removals held back", status.LastError);
        Assert.Equal(3, status.PendingRemoves);
        Assert.Null(status.LastSuccessAt);
    }

    [Fact]
    public async Task Reconcile_Failure_IsRecorded_AndRethrown()
    {
        var (cf, provider, _) = Build();
        cf.FailWith = HttpStatusCode.InternalServerError;
        var sync = provider.GetRequiredService<CloudflareAccessSync>();

        await Assert.ThrowsAsync<CloudflareApiException>(() => sync.ReconcileAsync());

        Assert.Contains("HTTP 500", sync.GetStatus().LastError);
    }

    [Fact]
    public async Task Disabled_MakesNoCalls_AndTheLoopReturns()
    {
        var (cf, provider, desired) = Build(FakeCloudflare.Settings(enabled: false));
        desired.Emails = new() { "a@example.com" };
        var sync = provider.GetRequiredService<CloudflareAccessSync>();

        Assert.False(sync.Enabled);
        await sync.ReconcileAsync();
        Assert.False(await sync.TryAddNowAsync("a@example.com", TimeSpan.FromSeconds(1)));
        sync.RequestSync();
        var hosted = provider.GetServices<IHostedService>().OfType<CloudflareAccessSyncService>().Single();
        await hosted.StartAsync(CancellationToken.None);
        await hosted.ExecuteTask!;

        Assert.Empty(cf.Requests);
        var status = sync.GetStatus();
        Assert.False(status.Enabled);
        Assert.Null(status.LastError);
    }

    [Fact]
    public void EnabledWithoutToken_IsNotEnabled_AndSaysWhy()
    {
        var settings = FakeCloudflare.Settings();
        settings.ApiToken = null;
        var (_, provider, _) = Build(settings);
        var sync = provider.GetRequiredService<CloudflareAccessSync>();

        Assert.False(sync.Enabled);
        Assert.Contains("ApiToken", sync.GetStatus().LastError);
    }

    [Fact]
    public async Task Service_RunsOnStart_AndAgainWhenWoken()
    {
        var (cf, provider, desired) = Build();
        desired.Emails = new() { "first@example.com" };
        var sync = provider.GetRequiredService<CloudflareAccessSync>();
        var hosted = provider.GetServices<IHostedService>().OfType<CloudflareAccessSyncService>().Single();
        using var cts = new CancellationTokenSource();

        await hosted.StartAsync(cts.Token);
        await WaitUntil(() => cf.Values.Contains("first@example.com"));
        desired.Emails = new() { "second@example.com" };
        sync.RequestSync();
        await WaitUntil(() => cf.Values.SequenceEqual(new[] { "second@example.com" }), TimeSpan.FromSeconds(10));
        await hosted.StopAsync(CancellationToken.None);
    }

    // ------------------------------------------------------------ inline add

    [Fact]
    public async Task InlineAdd_AppendsTheNormalizedEmail()
    {
        var (cf, provider, _) = Build();

        var added = await provider.GetRequiredService<ICloudflareAccessSync>().TryAddNowAsync(" New@Example.com ", TimeSpan.FromSeconds(5));

        Assert.True(added);
        Assert.Equal(new[] { "new@example.com" }, cf.Values);
    }

    [Fact]
    public async Task InlineAdd_GivesUpAtItsTimeout_AndNeverThrows()
    {
        var (cf, provider, _) = Build();
        cf.Delay = TimeSpan.FromSeconds(30);
        var sync = provider.GetRequiredService<ICloudflareAccessSync>();

        var watch = Stopwatch.StartNew();
        var added = await sync.TryAddNowAsync("slow@example.com", TimeSpan.FromMilliseconds(300));

        Assert.False(added);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    [Fact]
    public async Task InlineAdd_Error_IsSwallowed()
    {
        var (cf, provider, _) = Build();
        cf.FailWith = HttpStatusCode.Forbidden;

        Assert.False(await provider.GetRequiredService<ICloudflareAccessSync>().TryAddNowAsync("x@example.com", TimeSpan.FromSeconds(5)));
    }

    // ------------------------------------------------------------ desired set

    [Fact]
    public async Task DesiredSet_LiveInvites_PlusActiveInvitedOrAdmittedAccounts()
    {
        var now = new DateTime(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        await using var db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase("cf-desired-" + Guid.NewGuid()).Options);
        Invitation Personal(string email, DateTime? revoked = null, int uses = 0, int days = 7) => new()
        {
            Kind = InvitationKind.Personal, Email = email, CodeHash = Guid.NewGuid().ToString("N"), CodeHint = "abcd",
            MaxUses = 1, UseCount = uses, CreatedAt = now.AddDays(-1), ExpiresAt = now.AddDays(days), RevokedAt = revoked
        };
        db.Invitations.AddRange(
            Personal("Live@Example.com"),
            Personal("revoked@example.com", revoked: now),
            Personal("used@example.com", uses: 1),
            Personal("expired@example.com", days: -1),
            new Invitation { Kind = InvitationKind.Shared, CodeHash = "shared", CodeHint = "wxyz", MaxUses = 5, CreatedAt = now, ExpiresAt = now.AddDays(7) });
        User U(string name, string? email, bool active = true, DateTime? deleted = null, DateTime? admitted = null, int? invitedVia = null) =>
            new() { Username = name, Email = email, IsActive = active, DeletedAt = deleted, AdmittedAt = admitted, InvitedViaInvitationId = invitedVia, CreatedAt = now };
        db.Users.AddRange(
            U("grandfathered", "old@example.com", admitted: now.AddDays(-30)),
            U("invited", "invited@example.com", invitedVia: 1),
            U("frozen", "frozen@example.com", admitted: now),
            U("stranger", "stranger@example.com"),
            U("deactivated", "off@example.com", active: false, admitted: now),
            U("merged", "merged@example.com", active: false, deleted: now, admitted: now),
            U("gameonly", null, admitted: now));
        await db.SaveChangesAsync();
        db.Users.Single(u => u.Username == "frozen").IsFrozen = true;
        await db.SaveChangesAsync();

        var desired = await new CloudflareAccessDesiredSet(db).GetAsync(now);

        Assert.Equal(new[] { "frozen@example.com", "invited@example.com", "live@example.com", "old@example.com" }, desired.OrderBy(e => e));
    }

    private static async Task WaitUntil(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met");
            await Task.Delay(25);
        }
    }
}
