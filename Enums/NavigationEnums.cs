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
