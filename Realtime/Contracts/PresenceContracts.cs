namespace knkwebapi_v2.Realtime.Contracts;

/// <summary>
/// One online player in a game server's roster (DESIGN §5, §6.4): sent in <c>ReportRoster</c> on every (re)connect
/// and in <c>PlayerJoined</c>.
/// </summary>
public sealed class RosterPlayerDto
{
    /// <summary>Minecraft UUID with dashes.</summary>
    public string Uuid { get; set; } = string.Empty;

    public string? Name { get; set; }

    public string? World { get; set; }

    /// <summary>The plugin's cached user id, when it has one (informational; the API matches on the UUID).</summary>
    public int? UserId { get; set; }
}

/// <summary><c>PlayerLeft</c> argument.</summary>
public sealed class PlayerLeftDto
{
    public string Uuid { get; set; } = string.Empty;

    public string? World { get; set; }
}
