using knkwebapi_v2.Services;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#280 AM2 (DESIGN.md § 4.6.5): the dashboard read checks are memoised per request and cached
/// 30 s per (user, node); grant changes drop them.
/// </summary>
public class PermissionReadCacheTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private readonly ManualTime _time = new();
    private readonly Mock<IPermissionResolutionService> _permissions = new();
    private readonly PermissionReadCache _cache;

    private static readonly string[] Nodes = { "knk.admin.content", "knk.admin.world", "knk.admin.road" };

    public PermissionReadCacheTests()
    {
        _cache = new PermissionReadCache(_time, TimeSpan.FromSeconds(30));
    }

    private void Holds(int userId, string? node) =>
        _permissions.Setup(p => p.CheckAnyAsync(userId, It.IsAny<IReadOnlyList<string>>()))
            .ReturnsAsync((int _, IReadOnlyList<string> asked) => asked.Contains(node) ? node : null);

    [Fact]
    public async Task Answer_IsCachedFor30Seconds()
    {
        Holds(1, "knk.admin.world");
        Assert.True(await _cache.HoldsAnyAsync(_permissions.Object, 1, Nodes));
        Assert.True(await _cache.HoldsAnyAsync(_permissions.Object, 1, Nodes));
        _permissions.Verify(p => p.CheckAnyAsync(1, It.IsAny<IReadOnlyList<string>>()), Times.Once);

        _time.Now = _time.Now.AddSeconds(31);
        Holds(1, null);
        Assert.False(await _cache.HoldsAnyAsync(_permissions.Object, 1, Nodes));
        _permissions.Verify(p => p.CheckAnyAsync(1, It.IsAny<IReadOnlyList<string>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Refusals_AreCachedToo_PerNode()
    {
        Holds(1, null);
        Assert.False(await _cache.HoldsAnyAsync(_permissions.Object, 1, Nodes));
        // Another type whose read nodes were all checked already: no new lookup.
        Assert.False(await _cache.HoldsAnyAsync(_permissions.Object, 1, new[] { "knk.admin.road", "knk.admin.content" }));
        _permissions.Verify(p => p.CheckAnyAsync(1, It.IsAny<IReadOnlyList<string>>()), Times.Once);
        // A node not seen yet is checked alone.
        await _cache.HoldsAnyAsync(_permissions.Object, 1, new[] { "knk.admin.content", "knk.kit.manage" });
        _permissions.Verify(p => p.CheckAnyAsync(1, It.Is<IReadOnlyList<string>>(n => n.SequenceEqual(new[] { "knk.kit.manage" }))), Times.Once);
    }

    [Fact]
    public async Task ForgetUser_DropsOnlyThatUser()
    {
        Holds(1, null);
        Holds(2, "knk.admin.world");
        await _cache.HoldsAnyAsync(_permissions.Object, 1, Nodes);
        await _cache.HoldsAnyAsync(_permissions.Object, 2, Nodes);

        Holds(1, "knk.admin.content");
        _cache.ForgetUser(1);

        Assert.True(await _cache.HoldsAnyAsync(_permissions.Object, 1, Nodes));
        Assert.True(await _cache.HoldsAnyAsync(_permissions.Object, 2, Nodes));
        _permissions.Verify(p => p.CheckAnyAsync(2, It.IsAny<IReadOnlyList<string>>()), Times.Once);
    }

    [Fact]
    public async Task ForgetAll_DropsEveryone()
    {
        Holds(1, "knk.admin.world");
        await _cache.HoldsAnyAsync(_permissions.Object, 1, Nodes);
        Holds(1, null);
        _cache.ForgetAll();
        Assert.False(await _cache.HoldsAnyAsync(_permissions.Object, 1, Nodes));
    }

    [Fact]
    public async Task AForgetDuringACheck_WinsOverItsAnswer()
    {
        var gate = new TaskCompletionSource<string?>();
        _permissions.Setup(p => p.CheckAnyAsync(1, It.IsAny<IReadOnlyList<string>>())).Returns(gate.Task);
        var pending = _cache.HoldsAnyAsync(_permissions.Object, 1, Nodes);
        _cache.ForgetUser(1);
        gate.SetResult("knk.admin.world");
        Assert.True(await pending);

        Holds(1, null);
        Assert.False(await _cache.HoldsAnyAsync(_permissions.Object, 1, Nodes)); // the old answer wasn't stored
    }

    [Fact]
    public async Task RequestMemo_AnswersWithinTheRequest_WithoutTheCache()
    {
        var noCache = new PermissionReadCache(_time, TimeSpan.Zero);
        var http = new DefaultHttpContext();
        Holds(1, "knk.admin.world");
        await noCache.HoldsAnyAsync(_permissions.Object, 1, Nodes, http);
        await noCache.HoldsAnyAsync(_permissions.Object, 1, Nodes, http);
        _permissions.Verify(p => p.CheckAnyAsync(1, It.IsAny<IReadOnlyList<string>>()), Times.Once);

        await noCache.HoldsAnyAsync(_permissions.Object, 1, Nodes, new DefaultHttpContext());
        _permissions.Verify(p => p.CheckAnyAsync(1, It.IsAny<IReadOnlyList<string>>()), Times.Exactly(2));
    }

    [Fact]
    public async Task NoUserOrNoNodes_IsNo()
    {
        Assert.False(await _cache.HoldsAnyAsync(_permissions.Object, 0, Nodes));
        Assert.False(await _cache.HoldsAnyAsync(_permissions.Object, 1, Array.Empty<string>()));
        _permissions.VerifyNoOtherCalls();
    }
}
