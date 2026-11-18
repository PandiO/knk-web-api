namespace knkwebapi_v2.Enums;

/// <summary>
/// The values of <see cref="knkwebapi_v2.Models.AuditLogEntry.SubjectType"/> (PandiO/knk-workspace#50). Strings, not
/// an enum column, so a later subject type (a clan, a treasury) needs no migration; add its name here.
/// </summary>
public static class AuditSubjectTypes
{
    /// <summary>A permission group: changes to the group's own grants (GrantAdded, GrantUpdated, GrantRemoved).</summary>
    public const string PermissionGroup = "PermissionGroup";

    /// <summary>A gate structure: live gate commands from the web app (GateCommandSent, PandiO/knk-workspace#741).</summary>
    public const string GateStructure = "GateStructure";

    /// <summary>Longest allowed name (the column's max length).</summary>
    public const int MaxLength = 64;
}
