using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// A record whose references <c>GET api/EntityReferences/{type}/{id}/rows</c> lists: its CLR type name and the nodes
    /// that read it (any one). See <see cref="ReferenceRecordTypes"/>.
    /// </summary>
    public sealed record ReferenceRecordType(string TypeName, IReadOnlyList<string> ReadNodes);

    /// <summary>
    /// The records "Show what uses it" can list the references of (PandiO/knk-workspace#44, #289): every dashboard type
    /// (<see cref="DashboardAccess"/>, read nodes as there), plus the form configuration, whose delete on Entity setup is
    /// refused when another form opens it as a sub-form. Entity setup and the builders need <c>knk.admin.content</c>, so
    /// that is the configuration's read node here. Display configurations are not listed: nothing blocks their delete.
    /// </summary>
    public static class ReferenceRecordTypes
    {
        public const string FormConfiguration = "FormConfiguration";

        /// <summary>What reads a form configuration, its form steps and its form fields here: Entity setup's node.</summary>
        public static readonly IReadOnlyList<string> ConfigurationReadNodes = new[] { StaffPermissions.ManageContent };

        private static readonly ReferenceRecordType FormConfigurationRecord = new(FormConfiguration, ConfigurationReadNodes);

        /// <summary>The record type by name (case-insensitive), or null when its references aren't listed.</summary>
        public static ReferenceRecordType? Find(string? typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;
            var dashboard = DashboardAccess.Find(typeName);
            if (dashboard != null) return new ReferenceRecordType(dashboard.TypeName, dashboard.ReadNodes);
            return string.Equals(typeName.Trim(), FormConfiguration, StringComparison.OrdinalIgnoreCase) ? FormConfigurationRecord : null;
        }
    }
}
