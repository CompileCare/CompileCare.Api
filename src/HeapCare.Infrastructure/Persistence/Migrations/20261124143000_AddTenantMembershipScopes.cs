using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeapCare.Infrastructure.Persistence.Migrations
{
    public partial class AddTenantMembershipScopes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_departments_TenantId_HospitalId_Id",
                table: "departments",
                columns: new[] { "TenantId", "HospitalId", "Id" });

            migrationBuilder.CreateTable(
                name: "tenant_membership_scopes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MembershipId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: true),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_membership_scopes", x => x.Id);
                    table.CheckConstraint(
                        "CK_tenant_membership_scopes_shape",
                        """
                        ("ScopeType" = 'Tenant' AND "HospitalId" IS NULL AND "DepartmentId" IS NULL)
                        OR ("ScopeType" = 'Hospital' AND "HospitalId" IS NOT NULL AND "DepartmentId" IS NULL)
                        OR ("ScopeType" = 'Department' AND "HospitalId" IS NOT NULL AND "DepartmentId" IS NOT NULL)
                        """);
                });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_membership_scopes_TenantId_MembershipId",
                table: "tenant_membership_scopes",
                columns: new[] { "TenantId", "MembershipId" },
                unique: true,
                filter: "\"ScopeType\" = 'Tenant'");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_membership_scopes_TenantId_MembershipId_HospitalId",
                table: "tenant_membership_scopes",
                columns: new[] { "TenantId", "MembershipId", "HospitalId" },
                unique: true,
                filter: "\"ScopeType\" = 'Hospital'");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_membership_scopes_TenantId_MembershipId_DepartmentId",
                table: "tenant_membership_scopes",
                columns: new[] { "TenantId", "MembershipId", "DepartmentId" },
                unique: true,
                filter: "\"ScopeType\" = 'Department'");

            migrationBuilder.CreateIndex(
                name: "IX_tenant_membership_scopes_TenantId_HospitalId_DepartmentId",
                table: "tenant_membership_scopes",
                columns: new[] { "TenantId", "HospitalId", "DepartmentId" });

            migrationBuilder.AddForeignKey(
                name: "FK_tenant_membership_scopes_tenants_TenantId",
                table: "tenant_membership_scopes",
                column: "TenantId",
                principalTable: "tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_tenant_membership_scopes_tenant_memberships_TenantId_MembershipId",
                table: "tenant_membership_scopes",
                columns: new[] { "TenantId", "MembershipId" },
                principalTable: "tenant_memberships",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_tenant_membership_scopes_hospitals_TenantId_HospitalId",
                table: "tenant_membership_scopes",
                columns: new[] { "TenantId", "HospitalId" },
                principalTable: "hospitals",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_tenant_membership_scopes_departments_TenantId_HospitalId_DepartmentId",
                table: "tenant_membership_scopes",
                columns: new[] { "TenantId", "HospitalId", "DepartmentId" },
                principalTable: "departments",
                principalColumns: new[] { "TenantId", "HospitalId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // Existing memberships previously had tenant-wide facility access. Preserve
            // that behavior explicitly while all new memberships require a chosen scope.
            migrationBuilder.Sql(
                """
                INSERT INTO "tenant_membership_scopes"
                    ("Id", "TenantId", "MembershipId", "ScopeType", "HospitalId",
                     "DepartmentId", "CreatedAtUtc", "UpdatedAtUtc", "Version")
                SELECT
                    gen_random_uuid(),
                    membership."TenantId",
                    membership."Id",
                    'Tenant',
                    NULL,
                    NULL,
                    membership."CreatedAtUtc",
                    membership."UpdatedAtUtc",
                    gen_random_uuid()
                FROM "tenant_memberships" AS membership;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tenant_membership_scopes_departments_TenantId_HospitalId_DepartmentId",
                table: "tenant_membership_scopes");
            migrationBuilder.DropForeignKey(
                name: "FK_tenant_membership_scopes_hospitals_TenantId_HospitalId",
                table: "tenant_membership_scopes");
            migrationBuilder.DropForeignKey(
                name: "FK_tenant_membership_scopes_tenant_memberships_TenantId_MembershipId",
                table: "tenant_membership_scopes");
            migrationBuilder.DropForeignKey(
                name: "FK_tenant_membership_scopes_tenants_TenantId",
                table: "tenant_membership_scopes");

            migrationBuilder.DropTable(name: "tenant_membership_scopes");
            migrationBuilder.DropUniqueConstraint(
                name: "AK_departments_TenantId_HospitalId_Id",
                table: "departments");
        }
    }
}
