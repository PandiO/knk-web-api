using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

/// <summary>
/// Read-only picker row for the abstract PermissionHolder type. A holder is either a User or a
/// PermissionGroup; Name is the concrete subtype's human-readable username/group name.
/// </summary>
public class PermissionHolderListDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("holderType")]
    public string HolderType { get; set; } = null!;
}
