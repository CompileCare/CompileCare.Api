using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompileCare.Infrastructure.Persistence.Migrations;

partial class AddStaffProfilesAndAssignments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "staff_profiles",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StaffCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                GivenName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                MiddleName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                FamilyName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ProviderDesignation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_staff_profiles", x => x.Id);
                table.UniqueConstraint("AK_staff_profiles_TenantId_Id", x => new { x.TenantId, x.Id });
                table.ForeignKey(
                    name: "FK_staff_profiles_tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "staff_assignments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StaffProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                EffectiveTo = table.Column<DateOnly>(type: "date", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_staff_assignments", x => x.Id);
                table.UniqueConstraint("AK_staff_assignments_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint(
                    "CK_staff_assignments_effective_range",
                    "\"EffectiveTo\" IS NULL OR \"EffectiveTo\" >= \"EffectiveFrom\"");
                table.ForeignKey(
                    name: "FK_staff_assignments_departments_TenantId_HospitalId_DepartmentId",
                    columns: x => new { x.TenantId, x.HospitalId, x.DepartmentId },
                    principalTable: "departments",
                    principalColumns: new[] { "TenantId", "HospitalId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_staff_assignments_hospitals_TenantId_HospitalId",
                    columns: x => new { x.TenantId, x.HospitalId },
                    principalTable: "hospitals",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_staff_assignments_staff_profiles_TenantId_StaffProfileId",
                    columns: x => new { x.TenantId, x.StaffProfileId },
                    principalTable: "staff_profiles",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_staff_assignments_tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_staff_profiles_TenantId_StaffCode",
            table: "staff_profiles",
            columns: new[] { "TenantId", "StaffCode" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_staff_assignments_TenantId_StaffProfileId_HospitalId",
            table: "staff_assignments",
            columns: new[] { "TenantId", "StaffProfileId", "HospitalId" },
            unique: true,
            filter: "\"DepartmentId\" IS NULL AND \"IsActive\" = TRUE AND \"EffectiveTo\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_staff_assignments_TenantId_StaffProfileId_HospitalId_DepartmentId",
            table: "staff_assignments",
            columns: new[] { "TenantId", "StaffProfileId", "HospitalId", "DepartmentId" },
            unique: true,
            filter: "\"DepartmentId\" IS NOT NULL AND \"IsActive\" = TRUE AND \"EffectiveTo\" IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "staff_assignments");
        migrationBuilder.DropTable(name: "staff_profiles");
    }
}
