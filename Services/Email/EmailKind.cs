namespace knkwebapi_v2.Services.Email
{
    /// <summary>Every mail the API sends (DESIGN.md § 4.5). Each has a template pair under
    /// Services/Email/Templates and a throttle class, see <see cref="EmailKinds"/>.</summary>
    public enum EmailKind
    {
        Invitation,
        Welcome,
        MinecraftLinked,
        FinishLinking,
        PasswordReset,
        EmailChanged,
        EmailVerification,
        PrivacyDeletionConfirm,
        PrivacyDeletionScheduled,
        /// <summary>"Send test mail to me" on /admin/email.</summary>
        Test
    }

    /// <param name="Template">Base name of the .html/.txt pair, e.g. "password-reset".</param>
    /// <param name="Subject">Subject line; may hold {{placeholders}} (not HTML-encoded).</param>
    /// <param name="ThrottleClass">Kinds sharing a class share the per-recipient hourly limit.</param>
    /// <param name="Reason">The footer's "You get this mail because …" sentence.</param>
    public sealed record EmailKindInfo(string Template, string Subject, string ThrottleClass, string Reason);

    public static class EmailKinds
    {
        private static readonly IReadOnlyDictionary<EmailKind, EmailKindInfo> All = new Dictionary<EmailKind, EmailKindInfo>
        {
            [EmailKind.Invitation] = new("invitation", "You're invited to Knights & Kings", "invitation",
                "a Knights & Kings staff member invited this address."),
            [EmailKind.Welcome] = new("welcome", "Welcome to Knights & Kings", "onboarding",
                "you just created a Knights & Kings account."),
            [EmailKind.MinecraftLinked] = new("minecraft-linked", "Your Minecraft account is linked", "onboarding",
                "a Minecraft account was linked to your Knights & Kings account."),
            [EmailKind.FinishLinking] = new("finish-linking", "One step left: link your Minecraft account", "onboarding",
                "your Knights & Kings account isn't linked to Minecraft yet."),
            [EmailKind.PasswordReset] = new("password-reset", "Reset your Knights & Kings password", "password-reset",
                "someone asked to reset the password of the Knights & Kings account with this address."),
            [EmailKind.EmailChanged] = new("email-changed", "Your Knights & Kings email was changed", "account-notice",
                "this address was the email of a Knights & Kings account until just now."),
            [EmailKind.EmailVerification] = new("email-verification", "Confirm your email for Knights & Kings", "verification",
                "this address was entered on a Knights & Kings account."),
            [EmailKind.PrivacyDeletionConfirm] = new("privacy-deletion-confirm", "Confirm the deletion of your Knights & Kings data", "privacy",
                "someone asked to delete the Knights & Kings account with this address."),
            [EmailKind.PrivacyDeletionScheduled] = new("privacy-deletion-scheduled", "Your Knights & Kings data will be deleted", "privacy",
                "the deletion of the Knights & Kings account with this address was scheduled."),
            [EmailKind.Test] = new("test", "Knights & Kings test mail", "test",
                "you sent yourself a test mail from the Knights & Kings admin pages."),
        };

        public static EmailKindInfo Info(EmailKind kind) => All[kind];

        public static IEnumerable<EmailKind> Every => All.Keys;
    }
}
