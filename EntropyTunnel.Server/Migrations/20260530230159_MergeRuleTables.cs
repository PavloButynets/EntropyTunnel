using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EntropyTunnel.Server.Migrations
{
    /// <inheritdoc />
    public partial class MergeRuleTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chaos_rules");

            migrationBuilder.DropTable(
                name: "mock_rules");

            migrationBuilder.DropTable(
                name: "routing_rules");

            migrationBuilder.CreateTable(
                name: "rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    data = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_rules_agents_client_id",
                        column: x => x.client_id,
                        principalTable: "agents",
                        principalColumn: "client_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rules_client_id_type",
                table: "rules",
                columns: new[] { "client_id", "type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rules");

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
                name: "ix_chaos_rules_client_id",
                table: "chaos_rules",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_mock_rules_client_id",
                table: "mock_rules",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_routing_rules_client_id",
                table: "routing_rules",
                column: "client_id");
        }
    }
}
