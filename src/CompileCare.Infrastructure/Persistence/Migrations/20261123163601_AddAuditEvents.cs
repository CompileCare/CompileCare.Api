using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompileCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    RequestedTenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorIssuer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ActorSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: true),
                    HttpMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RouteTemplate = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                    table.CheckConstraint("CK_audit_events_outcome", "\"Outcome\" IN ('Succeeded', 'Denied', 'Rejected', 'Failed')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_ResourceType_ResourceId_OccurredAtUtc",
                table: "audit_events",
                columns: new[] { "ResourceType", "ResourceId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_TenantId_OccurredAtUtc",
                table: "audit_events",
                columns: new[] { "TenantId", "OccurredAtUtc" });

            // Enforce append-only semantics in PostgreSQL, including raw SQL writes.
            // Use a single-quoted function body so migration execution does not split
            // on the PL/pgSQL block's internal statement terminator.
            migrationBuilder.Sql(
                @"CREATE OR REPLACE FUNCTION prevent_audit_event_mutation()
                  RETURNS trigger
                  LANGUAGE plpgsql
                  AS 'BEGIN
                      RAISE EXCEPTION ''audit_events is append-only''
                          USING ERRCODE = ''55000'';
                  END;'");

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_audit_events_no_update_delete
                BEFORE UPDATE OR DELETE ON audit_events
                FOR EACH ROW
                EXECUTE FUNCTION prevent_audit_event_mutation();
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER trg_audit_events_no_truncate
                BEFORE TRUNCATE ON audit_events
                FOR EACH STATEMENT
                EXECUTE FUNCTION prevent_audit_event_mutation();
                """);
        }


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_events_no_truncate ON audit_events;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_events_no_update_delete ON audit_events;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS prevent_audit_event_mutation();");

            migrationBuilder.DropTable(
                name: "audit_events");
        }
    }
}
