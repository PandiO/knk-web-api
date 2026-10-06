namespace knkwebapi_v2.Services
{
    /// <summary>
    /// A permission grant write that would give a holder a second row for a node it already has
    /// (KNG-59: one row per (holder, node), enforced by a unique index). Controllers answer 409
    /// <c>{ code = "NodeTaken", message }</c>.
    /// </summary>
    public class PermissionGrantConflictException : InvalidOperationException
    {
        public int ExistingGrantId { get; }

        public PermissionGrantConflictException(int existingGrantId, string message) : base(message)
        {
            ExistingGrantId = existingGrantId;
        }
    }
}
