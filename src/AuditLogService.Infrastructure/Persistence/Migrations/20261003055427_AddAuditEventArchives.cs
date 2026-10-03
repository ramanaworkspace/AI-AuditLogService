using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditLogService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEventArchives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_event_archives",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_event_archives", x => x.event_id);
                    table.ForeignKey(
                        name: "FK_audit_event_archives_audit_events_event_id",
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
                name: "audit_event_archives");
        }
    }
}
