using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompileCare.Infrastructure.Persistence.Migrations
{
    public partial class AddTenantRegistry : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(63)", maxLength: 63, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenants", x => x.Id);
                    table.CheckConstraint("CK_tenants_slug_format", "\"Slug\" ~ '^[a-z0-9]+(-[a-z0-9]+)*$'");
                    table.CheckConstraint("CK_tenants_status", "\"Status\" IN ('Provisioning', 'Active', 'Suspended', 'Closed')");
                });

            migrationBuilder.CreateTable(
                name: "platform_operators",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_operators", x => x.Id);
                    table.CheckConstraint("CK_platform_operators_identity", "\"Issuer\" <> '' AND \"Subject\" <> ''");
                });

            migrationBuilder.CreateIndex("IX_tenants_Slug", "tenants", "Slug", unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_platform_operators_Issuer_Subject",
                table: "platform_operators",
                columns: new[] { "Issuer", "Subject" },
                unique: true);

            // Backfill all existing tenant IDs before adding foreign keys. Existing records
            // remain in their current tenant and are represented as active legacy tenants.
            migrationBuilder.Sql(
                """
                INSERT INTO "tenants"
                    ("Id", "Name", "Slug", "Status", "CreatedAtUtc", "UpdatedAtUtc", "Version")
                SELECT
                    source."TenantId",
                    'Legacy tenant',
                    'legacy-' || replace(source."TenantId"::text, '-', ''),
                    'Active',
                    CURRENT_TIMESTAMP,
                    CURRENT_TIMESTAMP,
                    gen_random_uuid()
                FROM (
                    SELECT "TenantId" FROM "tenant_memberships"
                    UNION
                    SELECT "TenantId" FROM "hospitals"
                    UNION
                    SELECT "TenantId" FROM "departments"
                    UNION
                    SELECT "TenantId" FROM "wards"
                    UNION
                    SELECT "TenantId" FROM "rooms"
                    UNION
                    SELECT "TenantId" FROM "beds"
                    UNION
                    SELECT "TenantId" FROM "audit_events" WHERE "TenantId" IS NOT NULL
                ) AS source
                WHERE source."TenantId" IS NOT NULL
                ON CONFLICT ("Id") DO NOTHING;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_hospitals_tenants_TenantId",
                table: "hospitals",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_departments_tenants_TenantId",
                table: "departments",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_wards_tenants_TenantId",
                table: "wards",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_rooms_tenants_TenantId",
                table: "rooms",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_beds_tenants_TenantId",
                table: "beds",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_tenant_memberships_tenants_TenantId",
                table: "tenant_memberships",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "FK_audit_events_tenants_TenantId",
                table: "audit_events",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey("FK_audit_events_tenants_TenantId", "audit_events");
            migrationBuilder.DropForeignKey("FK_tenant_memberships_tenants_TenantId", "tenant_memberships");
            migrationBuilder.DropForeignKey("FK_beds_tenants_TenantId", "beds");
            migrationBuilder.DropForeignKey("FK_rooms_tenants_TenantId", "rooms");
            migrationBuilder.DropForeignKey("FK_wards_tenants_TenantId", "wards");
            migrationBuilder.DropForeignKey("FK_departments_tenants_TenantId", "departments");
            migrationBuilder.DropForeignKey("FK_hospitals_tenants_TenantId", "hospitals");
            migrationBuilder.DropTable("platform_operators");
            migrationBuilder.DropTable("tenants");
        }
    }
}
