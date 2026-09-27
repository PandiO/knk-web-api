using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <summary>
    /// DB-level immutability of the currency ledger (currency-payments DESIGN.md §3.2, §4 D7):
    /// BEFORE UPDATE / BEFORE DELETE triggers on currency_transactions and currency_entries that
    /// raise SQLSTATE 45000, so not even a hand-written UPDATE/DELETE can rewrite history.
    /// Corrections are reversals. (MySQL doesn't fire triggers for cascaded FK actions; the ledger
    /// has no cascading FKs.)
    /// <para>
    /// Requires the TRIGGER privilege, and with binary logging on (the MySQL default) also SUPER
    /// or <c>log_bin_trust_function_creators = 1</c>; otherwise it fails with error 1419/1142.
    /// Kept separate from AddCurrencyLedger so it can be skipped (DESIGN.md §5 Q7): set the
    /// environment variable <c>KNK_SKIP_LEDGER_TRIGGERS=true</c> when running
    /// <c>dotnet ef database update</c>; the migration is then recorded as applied without
    /// creating the triggers, and immutability rests on the append-only repository plus the
    /// reconciler. To add the triggers later, run the four CREATE TRIGGER statements below by hand.
    /// </para>
    /// </summary>
    public partial class AddCurrencyLedgerImmutabilityTriggers : Migration
    {
        public const string SkipEnvironmentVariable = "KNK_SKIP_LEDGER_TRIGGERS";

        private static readonly (string Table, string Event)[] Triggers =
        {
            ("currency_transactions", "UPDATE"),
            ("currency_transactions", "DELETE"),
            ("currency_entries", "UPDATE"),
            ("currency_entries", "DELETE"),
        };

        private static string TriggerName(string table, string evt) => $"trg_{table}_no_{evt.ToLowerInvariant()}";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (string.Equals(System.Environment.GetEnvironmentVariable(SkipEnvironmentVariable), "true", System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (var (table, evt) in Triggers)
            {
                // Single-statement trigger body, so no DELIMITER juggling is needed.
                migrationBuilder.Sql(
                    $"CREATE TRIGGER `{TriggerName(table, evt)}` BEFORE {evt} ON `{table}` FOR EACH ROW " +
                    $"SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'The currency ledger is append-only: {evt} on {table} is not allowed (post a reversal instead).';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, evt) in Triggers)
            {
                migrationBuilder.Sql($"DROP TRIGGER IF EXISTS `{TriggerName(table, evt)}`;");
            }
        }
    }
}
