using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// A single grant or explicit deny of a permission node, owned by a <see cref="PermissionHolder"/>
/// (a <see cref="User"/>'s own direct grant, or a <see cref="PermissionGroup"/>'s grant inherited by
/// its members). <see cref="Node"/> may be an exact node (e.g. "knk.gate.open") or a wildcard
/// (e.g. "knk.gate.*") — see docs/specs/user-features/DESIGN.md §2.2 for resolution/precedence rules.
/// </summary>
[FormConfigurableEntity("PermissionGrant")]
public class PermissionGrant
{
    public int Id { get; set; }

    [NavigationPair(nameof(Holder))]
    [RelatedEntityField(typeof(PermissionHolder))]
    public int HolderId { get; set; }

    [RelatedEntityField(typeof(PermissionHolder))]
    public PermissionHolder Holder { get; set; } = null!;

    /// <summary>Dot-path permission node, e.g. "knk.gate.open" or "customenchantments.*".</summary>
    public string Node { get; set; } = null!;

    /// <summary>true = grant, false = explicit deny.</summary>
    public bool Value { get; set; } = true;

    /// <summary>Null = never expires. Expired grants are excluded from resolution.</summary>
    public DateTime? ExpiresAt { get; set; }
}
