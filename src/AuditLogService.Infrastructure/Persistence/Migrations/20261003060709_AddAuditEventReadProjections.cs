using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditLogService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEventReadProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_event_read_projections",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<JsonElement>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_event_read_projections", x => x.event_id);
                    table.ForeignKey(
                        name: "FK_audit_event_read_projections_audit_events_event_id",
                        column: x => x.event_id,
                        principalTable: "audit_events",
                        principalColumn: "event_id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_event_read_projections");
        }
    }
}
