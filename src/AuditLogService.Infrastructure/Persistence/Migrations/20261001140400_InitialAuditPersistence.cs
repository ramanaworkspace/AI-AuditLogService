using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuditLogService.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAuditPersistence : Migration
    {
        private static readonly string[] ChainMetadataColumns =
            ["chain_id", "head_hash", "head_sequence_number"];
        private static readonly string[] ResourceIndexColumns =
            ["resource_type", "resource_id"];
        private static readonly object[] ChainMetadataValues =
            [1, "GENESIS", 0L];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    resource_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    resource_id = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    payload = table.Column<JsonElement>(type: "jsonb", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    previous_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.event_id);
                    table.CheckConstraint("ck_audit_events_actor_id_not_empty", "length(btrim(actor_id)) > 0");
                    table.CheckConstraint("ck_audit_events_content_hash_format", "content_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_audit_events_event_type_not_empty", "length(btrim(event_type)) > 0");
                    table.CheckConstraint("ck_audit_events_payload_object", "jsonb_typeof(payload) = 'object'");
                    table.CheckConstraint("ck_audit_events_previous_hash_format", "previous_hash = 'GENESIS' OR previous_hash ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("ck_audit_events_resource_id_not_empty", "length(btrim(resource_id)) > 0");
                    table.CheckConstraint("ck_audit_events_resource_type_not_empty", "length(btrim(resource_type)) > 0");
                    table.CheckConstraint("ck_audit_events_sequence_positive", "sequence_number > 0");
                });

            migrationBuilder.CreateTable(
                name: "chain_metadata",
                columns: table => new
                {
                    chain_id = table.Column<int>(type: "integer", nullable: false),
                    head_sequence_number = table.Column<long>(type: "bigint", nullable: false),
                    head_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chain_metadata", x => x.chain_id);
                    table.CheckConstraint("ck_chain_metadata_head_hash_format", "(head_sequence_number = 0 AND head_hash = 'GENESIS') OR (head_sequence_number > 0 AND head_hash ~ '^[0-9a-f]{64}$')");
                    table.CheckConstraint("ck_chain_metadata_head_sequence_nonnegative", "head_sequence_number >= 0");
                    table.CheckConstraint("ck_chain_metadata_single_global_chain", "chain_id = 1");
                });

            migrationBuilder.InsertData(
                table: "chain_metadata",
                columns: ChainMetadataColumns,
                values: ChainMetadataValues);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_id",
                table: "audit_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_event_type",
                table: "audit_events",
                column: "event_type");

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_resource_type_resource_id",
                table: "audit_events",
                columns: ResourceIndexColumns);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_timestamp",
                table: "audit_events",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "ux_audit_events_sequence_number",
                table: "audit_events",
                column: "sequence_number",
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE FUNCTION reject_audit_event_mutation()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                BEGIN
                    RAISE EXCEPTION 'audit_events is append-only'
                        USING ERRCODE = '55000';
                END;
                $function$;

                CREATE TRIGGER trg_audit_events_append_only
                BEFORE UPDATE OR DELETE ON audit_events
                FOR EACH ROW
                EXECUTE FUNCTION reject_audit_event_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DROP TRIGGER IF EXISTS trg_audit_events_append_only ON audit_events;
                DROP FUNCTION IF EXISTS reject_audit_event_mutation();
                """);

            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "chain_metadata");
        }
    }
}
