using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// KNG-268: a gate block scan the plugin can't run is refused when the world task is created,
    /// with the reason the form shows, instead of failing on the server a moment later.
    /// </summary>
    public class GateScanNotReadyException : Exception
    {
        public GateScanNotReadyException(string message) : base(message)
        {
        }
    }

    public static class GateScanReadiness
    {
        /// <returns>Why the door can't be scanned yet, or null when it can.</returns>
        public static string? Problem(GateDoorDto door, bool openScan)
        {
            var doorRef = $"'{door.Name}' (id {door.Id})";
            switch (door.GeometryDefinitionMode)
            {
                case GeometryDefinitionMode.REGION:
                    var regionData = openScan ? door.OpenedRegionData : door.ClosedRegionData;
                    if (string.IsNullOrWhiteSpace(regionData))
                    {
                        var slot = openScan ? "opened" : "closed";
                        return $"Door {doorRef} has no {slot} region yet. Build the {slot} door, select it with WorldEdit "
                            + $"and run /gatedoor capture {door.Id} {slot} in game, then scan again.";
                    }
                    break;
                case GeometryDefinitionMode.FLOOD_FILL:
                    if (openScan)
                    {
                        return $"Door {doorRef} uses flood fill, which has no open-door scan. Use plane grid or region "
                            + "for a door whose shape changes when it opens.";
                    }
                    break;
            }

            if (door.GeometryDefinitionMode != GeometryDefinitionMode.FLOOD_FILL && door.AnchorPointId == null)
            {
                return $"Door {doorRef} has no anchor yet. Save the door's anchor first, then scan.";
            }

            return null;
        }
    }
}
