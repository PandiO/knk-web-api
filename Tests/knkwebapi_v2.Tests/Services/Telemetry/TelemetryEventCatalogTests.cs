using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Telemetry;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Telemetry;

/// <summary>
/// The diagnostic event allowlist (DESIGN.md §F.12; link 6 acceptance criterion 1): every baseline
/// and enhanced family of the design is listed, names follow the convention, and no payload key can
/// carry forbidden data (chat, command arguments, IPs, tokens, raw bodies, exception messages).
/// </summary>
public class TelemetryEventCatalogTests
{
    [Theory]
    [InlineData("session.join"), InlineData("session.leave"), InlineData("session.afk_changed")]
    [InlineData("menu.opened"), InlineData("menu.action"), InlineData("command.result")]
    [InlineData("siege.lobby_join_attempt"), InlineData("siege.vote_cast"), InlineData("siege.team_assignment")]
    [InlineData("siege.match_join"), InlineData("siege.match_leave"), InlineData("siege.match_phase")]
    [InlineData("siege.objective_captured"), InlineData("siege.gate_destroyed"), InlineData("discovery.granted")]
    [InlineData("currency.posting"), InlineData("api.call_failed"), InlineData("telemetry.dropped")]
    public void EveryBaselineFamilyOfTheDesign_IsListedAsBaseline(string name)
    {
        var def = TelemetryEventCatalog.Find(name);

        Assert.NotNull(def);
        Assert.Equal(TelemetryLevel.Baseline, def!.Level);
    }

    [Theory]
    [InlineData("movement.sample"), InlineData("menu.click"), InlineData("combat.hit"), InlineData("gate.hit")]
    public void EnhancedFamilies_AreEnhancedOnly(string name)
    {
        Assert.Equal(TelemetryLevel.Enhanced, TelemetryEventCatalog.Find(name)!.Level);
        Assert.Contains(name, TelemetryEventCatalog.PluginEventNames(TelemetryLevel.Enhanced));
        Assert.DoesNotContain(name, TelemetryEventCatalog.PluginEventNames(TelemetryLevel.Baseline));
    }

    [Fact]
    public void Names_FollowTheConvention_AndPayloadsAreBounded()
    {
        Assert.All(TelemetryEventCatalog.All, def =>
        {
            Assert.Matches(TelemetryEventCatalog.NamePattern, def.Name);
            Assert.InRange(def.PayloadKeys.Count, 0, TelemetryEventCatalog.MaxPayloadKeys);
        });
    }

    [Fact]
    public void NoPayloadKey_CanCarryForbiddenData()
    {
        var forbidden = new[] { "message", "text", "chat", "arg", "ip", "address", "token", "key", "password", "secret",
            "body", "header", "email", "inventory", "stack", "content" };

        Assert.All(TelemetryEventCatalog.All.SelectMany(d => d.PayloadKeys.Select(k => (d.Name, Key: k))), entry =>
        {
            var lower = entry.Key.ToLowerInvariant();
            // "itemKey"/"menuKey" identify menu items, "context" is a statistics context key.
            var allowed = lower is "menukey" or "itemkey" or "parentmenukey" or "context";
            Assert.False(!allowed && forbidden.Any(f => lower.Contains(f)), $"{entry.Name}.{entry.Key} looks like forbidden data");
        });
    }

    [Fact]
    public void ApiEvents_AreNotPluginEvents()
    {
        Assert.Equal(TelemetrySource.Api, TelemetryEventCatalog.Find("api.request_failed")!.Source);
        Assert.DoesNotContain("api.request_failed", TelemetryEventCatalog.PluginEventNames(TelemetryLevel.Baseline));
        Assert.Null(TelemetryEventCatalog.Find("chat.message"));
    }

    [Theory]
    [InlineData("SIEGE_FULL", true)]
    [InlineData("http_500", true)]
    [InlineData("not allowed", false)]
    [InlineData("/pay bob 100", false)]
    [InlineData("", false)]
    public void Codes_AreStableTokensOnly(string code, bool valid)
    {
        Assert.Equal(valid, TelemetryEventCatalog.CodePattern.IsMatch(code));
    }
}
