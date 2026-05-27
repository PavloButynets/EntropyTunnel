using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntropyTunnel.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    account_id = table.Column<string>(type: "text", nullable: false),
                    password = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_accounts", x => x.account_id);
                });

            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    client_id = table.Column<string>(type: "text", nullable: false),
                    account_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agents", x => x.client_id);
                    table.ForeignKey(
                        name: "fk_agents_accounts_account_id",
                        column: x => x.account_id,
                        principalTable: "accounts",
                        principalColumn: "account_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chaos_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chaos_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_chaos_rules_agents_client_id",
                        column: x => x.client_id,
                        principalTable: "agents",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "mock_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mock_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_mock_rules_agents_client_id",
                        column: x => x.client_id,
                        principalTable: "agents",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "request_log",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    data = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_request_log", x => x.request_id);
                    table.ForeignKey(
                        name: "fk_request_log_agents_client_id",
                        column: x => x.client_id,
                        principalTable: "agents",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "routing_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routing_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_routing_rules_agents_client_id",
                        column: x => x.client_id,
                        principalTable: "agents",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agents_account_id",
                table: "agents",
                column: "account_id");

            migrationBuilder.CreateIndex(
                name: "ix_chaos_rules_client_id",
                table: "chaos_rules",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_mock_rules_client_id",
                table: "mock_rules",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_request_log_client_id_timestamp",
                table: "request_log",
                columns: new[] { "client_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_routing_rules_client_id",
                table: "routing_rules",
                column: "client_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chaos_rules");

            migrationBuilder.DropTable(
                name: "mock_rules");

            migrationBuilder.DropTable(
                name: "request_log");

            migrationBuilder.DropTable(
                name: "routing_rules");

            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "accounts");
        }
    }
}
