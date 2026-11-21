namespace knkwebapi_v2.Enums;

/// <summary>
/// Where <c>/navigate &lt;domain&gt;</c> leads when the player names no mode (KNG-73,
/// docs/specs/navigation/DESIGN.md §6.1): the domain's spawn Location, or the closest point of its
/// WorldGuard region along the cheapest road route. The player can still pick either with the
/// <c>spawn</c> / <c>region</c> word. Stored as a string, serialized by name.
/// </summary>
public enum NavigationDestinationMode
{
    Spawn,
    Region
}

/// <summary>
/// Whether a domain's entry/exit rule (AllowEntry/AllowExit) keeps the road router off the roads in its
/// region (rev. 7 Part C, KNG-92, docs/specs/navigation/REV7_PROPOSAL.md §4). <c>Ignored</c> is for domains
/// along a public street, such as houses and shops: the router routes past them and the rule still holds at
/// the border, for teleports and on the walk path to the door. Stored as a string, serialized by name.
/// </summary>
public enum RoadAccessRule
{
    Applies,
    Ignored
}

/// <summary>
/// How the navigation compass item's display name is compared with the configured one (KNG-234):
/// <c>Exact</c> (the whole name, ignoring colour codes and case) or <c>Contains</c> (part of it).
/// Stored as a string, serialized by name.
/// </summary>
public enum NavigationCompassNameMatch
{
    Exact,
    Contains
}

/// <summary>
/// A permission group's default <c>/navigate</c> route mode (PandiO/knk-workspace#852, on top of the route modes of
/// #707): the mode the plugin uses for members who type no mode word. The names match the plugin's mode words
/// (knk-core <c>RouteMode</c>, which reads them in any case). Stored as a string, serialized by name.
/// </summary>
public enum RouteMode
{
    /// <summary>Length only.</summary>
    Shortest,
    /// <summary>Walking effort: stairs, ladders, doors, water and the road class.</summary>
    Efficient,
    /// <summary>Efficient plus the road and landmark preference weights (the route without a mode word).</summary>
    Preferred
}
