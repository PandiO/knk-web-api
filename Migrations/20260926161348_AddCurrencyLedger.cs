using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace knkwebapi_v2.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Currency ledger, currency-payments IMPLEMENTATION_PLAN.md Phase 1 (DESIGN.md §3.2,
            // KNG-23 folded in). The ledger starts empty (developer decision, DESIGN.md §5 resolved
            // item 11): no opening-balance or audit-log backfill, so existing balances are the
            // starting point of each user's BalanceBefore chain. The immutability triggers are a
            // separate migration (AddCurrencyLedgerImmutabilityTriggers) so they can be skipped on
            // a server where the DB user can't create triggers.
            migrationBuilder.AddColumn<string>(
                name: "TransferLockReason",
                table: "users",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true,
                collation: "utf8mb4_general_ci")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "TransferLockedAt",
                table: "users",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "currency_alerts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Rule = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Severity = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    TransactionId = table.Column<long>(type: "bigint", nullable: true),
                    DetailsJson = table.Column<string>(type: "json", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    AckedByUserId = table.Column<int>(type: "int", nullable: true),
                    AckedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "currency_pending_transfers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PublicId = table.Column<string>(type: "char(26)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SenderUserId = table.Column<int>(type: "int", nullable: false),
                    RecipientUserId = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdempotencyKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResultTransactionId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "currency_policies",
                columns: table => new
                {
                    Currency = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    TransfersEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Transferable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MinTransfer = table.Column<long>(type: "bigint", nullable: false),
                    MaxTransfer = table.Column<long>(type: "bigint", nullable: false),
                    DailySendCap = table.Column<long>(type: "bigint", nullable: false),
                    DailyReceiveCap = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmThreshold = table.Column<long>(type: "bigint", nullable: false),
                    ConfirmTtlSeconds = table.Column<int>(type: "int", nullable: false),
                    CooldownSeconds = table.Column<int>(type: "int", nullable: false),
                    MaxTransfersPerHour = table.Column<int>(type: "int", nullable: false),
                    MinSenderAccountAgeHours = table.Column<int>(type: "int", nullable: false),
                    MinSenderTitleBracketId = table.Column<int>(type: "int", nullable: true),
                    TransferFeeBasisPoints = table.Column<int>(type: "int", nullable: false),
                    MaxBalance = table.Column<long>(type: "bigint", nullable: false),
                    AdminDailyGrantCapPerActor = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Currency);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "currency_transactions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PublicId = table.Column<string>(type: "char(26)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Kind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    ReasonCode = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Reason = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceType = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceRef = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdempotencyScope = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IdempotencyKey = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_bin")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestHash = table.Column<string>(type: "char(64)", nullable: false, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Initiator = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    InitiatorUserId = table.Column<int>(type: "int", nullable: true),
                    InitiatorComponent = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FromUserId = table.Column<int>(type: "int", nullable: true),
                    ToUserId = table.Column<int>(type: "int", nullable: true),
                    MetadataJson = table.Column<string>(type: "json", nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CorrelationId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReversesTransactionId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.ForeignKey(
                        name: "FK_currency_transactions_currency_transactions_ReversesTransact~",
                        column: x => x.ReversesTransactionId,
                        principalTable: "currency_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateTable(
                name: "currency_entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    TransactionId = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    AccountKind = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    SystemAccount = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: true, collation: "utf8mb4_general_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Operation = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    BalanceBefore = table.Column<long>(type: "bigint", nullable: true),
                    BalanceAfter = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PRIMARY", x => x.Id);
                    table.CheckConstraint("CK_currency_entries_Account", "(`AccountKind` = 0 AND `UserId` IS NOT NULL AND `SystemAccount` IS NULL AND `BalanceBefore` IS NOT NULL AND `BalanceAfter` IS NOT NULL) OR (`AccountKind` = 1 AND `UserId` IS NULL AND `SystemAccount` IS NOT NULL AND `BalanceBefore` IS NULL AND `BalanceAfter` IS NULL)");
                    table.CheckConstraint("CK_currency_entries_Balances", "`BalanceAfter` IS NULL OR (`BalanceBefore` >= 0 AND `BalanceAfter` >= 0 AND `BalanceBefore` + `Amount` = `BalanceAfter`)");
                    table.ForeignKey(
                        name: "FK_currency_entries_currency_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "currency_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_currency_alerts_AckedAt_CreatedAt",
                table: "currency_alerts",
                columns: new[] { "AckedAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_alerts_UserId_CreatedAt",
                table: "currency_alerts",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_entries_SystemAccount_Currency_TransactionId",
                table: "currency_entries",
                columns: new[] { "SystemAccount", "Currency", "TransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_entries_TransactionId",
                table: "currency_entries",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_currency_entries_UserId_Currency_Id",
                table: "currency_entries",
                columns: new[] { "UserId", "Currency", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_pending_transfers_IdempotencyKey",
                table: "currency_pending_transfers",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_pending_transfers_PublicId",
                table: "currency_pending_transfers",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_pending_transfers_SenderUserId_Status",
                table: "currency_pending_transfers",
                columns: new[] { "SenderUserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_transactions_CorrelationId",
                table: "currency_transactions",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_currency_transactions_CreatedAt",
                table: "currency_transactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_currency_transactions_FromUserId_CreatedAt",
                table: "currency_transactions",
                columns: new[] { "FromUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_transactions_IdempotencyScope_IdempotencyKey",
                table: "currency_transactions",
                columns: new[] { "IdempotencyScope", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_transactions_InitiatorUserId_CreatedAt",
                table: "currency_transactions",
                columns: new[] { "InitiatorUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_transactions_PublicId",
                table: "currency_transactions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_transactions_ReasonCode_CreatedAt",
                table: "currency_transactions",
                columns: new[] { "ReasonCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_currency_transactions_ReversesTransactionId",
                table: "currency_transactions",
                column: "ReversesTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_currency_transactions_ToUserId_CreatedAt",
                table: "currency_transactions",
                columns: new[] { "ToUserId", "CreatedAt" });

            // Default policies (DESIGN.md §3.5, developer decisions §5 resolved items 2, 3, 5):
            // gems never transferable, 0% fee, senders ≥ 48 h old with title ≥ Peasant (bracket 1).
            var now = new DateTime(2026, 9, 26, 0, 0, 0, DateTimeKind.Utc);
            migrationBuilder.InsertData(
                table: "currency_policies",
                columns: new[]
                {
                    "Currency", "TransfersEnabled", "Transferable", "MinTransfer", "MaxTransfer", "DailySendCap",
                    "DailyReceiveCap", "ConfirmThreshold", "ConfirmTtlSeconds", "CooldownSeconds", "MaxTransfersPerHour",
                    "MinSenderAccountAgeHours", "MinSenderTitleBracketId", "TransferFeeBasisPoints", "MaxBalance",
                    "AdminDailyGrantCapPerActor", "UpdatedAt", "UpdatedByUserId"
                },
                values: new object[,]
                {
                    { (byte)0, true, true, 10L, 1_000_000L, 2_000_000L, 4_000_000L, 100_000L, 60, 5, 20, 48, 1, 0, 999_999_999L, 5_000_000L, now, null },
                    { (byte)1, true, false, 1L, 100L, 500L, 500L, 10L, 60, 5, 10, 48, 1, 0, 999_999L, 1_000L, now, null }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "currency_alerts");

            migrationBuilder.DropTable(
                name: "currency_entries");

            migrationBuilder.DropTable(
                name: "currency_pending_transfers");

            migrationBuilder.DropTable(
                name: "currency_policies");

            migrationBuilder.DropTable(
                name: "currency_transactions");

            migrationBuilder.DropColumn(
                name: "TransferLockReason",
                table: "users");

            migrationBuilder.DropColumn(
                name: "TransferLockedAt",
                table: "users");
        }
    }
}
