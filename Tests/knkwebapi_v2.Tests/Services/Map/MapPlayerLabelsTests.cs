using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Map;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Map;

/// <summary>
/// Overview map L4 (PandiO/knk-workspace#738, O23, A15): name labels follow the plugin's <c>ChatLineFormat</c> (the same
/// cases as knk-plugin <c>ChatLineFormatTest</c>): rank tag, braced title (tier first for members), name and colours.
/// </summary>
public sealed class MapPlayerLabelsTests
{
    private static UserDto User(string? title = null, string? tier = null, string? primary = null, string? secondary = null,
        int? primaryGroup = null, int? secondaryGroup = null) => new()
    {
        Username = "Pandi", TitleName = title, PremiumTierName = tier, ChatPrimaryColor = primary, ChatSecondaryColor = secondary,
        ChatPrimaryColorGroupId = primaryGroup, ChatSecondaryColorGroupId = secondaryGroup,
    };

    private static (string?, string?, bool, string, string, string) Parts(MapPlayerLabelDto l) =>
        (l.RankTag, l.Braced, l.BracedBold, l.Name, l.BracketColor, l.TextColor);

    [Fact]
    public void Owner_FixedOwnerColours_TitleBold() // A15 owner
    {
        var label = MapPlayerLabels.Build(MapPlayerLabels.Rank.Owner, "Pandi", User(title: "King", primary: "&b", secondary: "&c"));

        // Colours set only by the Default group (no group id) do not replace the fixed owner style.
        Assert.Equal(("OWNER", "King", true, "Pandi", "&6", "&5"), Parts(label));
    }

    [Fact]
    public void Staff_AGroupsOwnChatColourReplacesTheFixedOne_FieldByField() // A15 staff, #260
    {
        var label = MapPlayerLabels.Build(MapPlayerLabels.Rank.Staff, "Mod", User(title: "Knight", primary: "&e&l", primaryGroup: 4));

        Assert.Equal(("STAFF", "Knight", true, "Mod", "&3", "&e&l"), Parts(label));
    }

    [Fact]
    public void PremiumMember_TierBeforeTitle_GroupColours() // A15 premium
    {
        var label = MapPlayerLabels.Build(MapPlayerLabels.Rank.Member, "Rich", User(title: "Squire", tier: "Noble", primary: "&d", secondary: "&5"));

        Assert.Equal(((string?)null, "Noble Squire", false, "Rich", "&5", "&d"), Parts(label));
    }

    [Fact]
    public void DefaultMember_TitleOnly_DefaultColoursWithoutGroupColours() // A15 default
    {
        Assert.Equal(((string?)null, "Peasant", false, "Joe", "&2", "&a"),
            Parts(MapPlayerLabels.Build(MapPlayerLabels.Rank.Member, "Joe", User(title: "Peasant"))));
        Assert.Equal(((string?)null, "Noble", false, "Joe", "&2", "&a"),
            Parts(MapPlayerLabels.Build(MapPlayerLabels.Rank.Member, "Joe", User(tier: "Noble", primary: " "))));
        Assert.Equal(((string?)null, (string?)null, false, "Joe", "&2", "&a"),
            Parts(MapPlayerLabels.Build(MapPlayerLabels.Rank.Member, "Joe", null)));
    }

    [Fact]
    public void MinigameTagAndArenaLabel() // O31, A14
    {
        Assert.Equal("Siege", MapPlayerLabels.MinigameTag("siege"));
        Assert.Equal("[Siege] 12 players", MapPlayerLabels.ArenaLabel("Siege", 12));
        Assert.Equal("[Siege] 1 player", MapPlayerLabels.ArenaLabel("Siege", 1));
    }
}
