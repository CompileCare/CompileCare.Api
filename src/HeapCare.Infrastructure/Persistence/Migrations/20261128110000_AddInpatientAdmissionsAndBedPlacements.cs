using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeapCare.Infrastructure.Persistence.Migrations;

[DbContext(typeof(HeapCareDbContext))]
[Migration("20261128110000_AddInpatientAdmissionsAndBedPlacements")]
public partial class AddInpatientAdmissionsAndBedPlacements : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        HeapCareDbContextModelSnapshot.BuildModelForMigration(modelBuilder);

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint(
            name: "AK_wards_TenantId_DepartmentId_Id",
            table: "wards",
            columns: new[] { "TenantId", "DepartmentId", "Id" });

        migrationBuilder.AddUniqueConstraint(
            name: "AK_rooms_TenantId_WardId_Id",
            table: "rooms",
            columns: new[] { "TenantId", "WardId", "Id" });

        migrationBuilder.AddUniqueConstraint(
            name: "AK_beds_TenantId_RoomId_Id",
            table: "beds",
            columns: new[] { "TenantId", "RoomId", "Id" });

        migrationBuilder.CreateTable(
            name: "inpatient_admissions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AdmittedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DischargedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inpatient_admissions", x => x.Id);
                table.UniqueConstraint(
                    "AK_inpatient_admissions_TenantId_HospitalId_Id",
                    x => new { x.TenantId, x.HospitalId, x.Id });
                table.CheckConstraint(
                    "CK_inpatient_admissions_status",
                    "\"Status\" IN ('Active', 'Cancelled', 'Discharged')");
                table.CheckConstraint(
                    "CK_inpatient_admissions_lifecycle",
                    """
                    ("Status" = 'Active' AND "CancelledAtUtc" IS NULL AND "DischargedAtUtc" IS NULL)
                    OR ("Status" = 'Cancelled' AND "CancelledAtUtc" IS NOT NULL AND "DischargedAtUtc" IS NULL)
                    OR ("Status" = 'Discharged' AND "DischargedAtUtc" IS NOT NULL AND "CancelledAtUtc" IS NULL)
                    """);
                table.ForeignKey(
                    name: "FK_inpatient_admissions_tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inpatient_admissions_hospitals_TenantId_HospitalId",
                    columns: x => new { x.TenantId, x.HospitalId },
                    principalTable: "hospitals",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inpatient_admissions_patients_TenantId_PatientId",
                    columns: x => new { x.TenantId, x.PatientId },
                    principalTable: "patients",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inpatient_admissions_hospital_patient_registrations",
                    columns: x => new { x.TenantId, x.HospitalId, x.PatientId },
                    principalTable: "hospital_patient_registrations",
                    principalColumns: new[] { "TenantId", "HospitalId", "PatientId" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "inpatient_bed_placements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                AdmissionId = table.Column<Guid>(type: "uuid", nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                WardId = table.Column<Guid>(type: "uuid", nullable: false),
                RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                BedId = table.Column<Guid>(type: "uuid", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                EndedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                EndReason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_inpatient_bed_placements", x => x.Id);
                table.CheckConstraint(
                    "CK_inpatient_bed_placements_time_range",
                    "\"EndedAtUtc\" IS NULL OR \"EndedAtUtc\" > \"StartedAtUtc\"");
                table.ForeignKey(
                    name: "FK_inpatient_bed_placements_tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inpatient_bed_placements_admissions",
                    columns: x => new { x.TenantId, x.HospitalId, x.AdmissionId },
                    principalTable: "inpatient_admissions",
                    principalColumns: new[] { "TenantId", "HospitalId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inpatient_bed_placements_hospitals",
                    columns: x => new { x.TenantId, x.HospitalId },
                    principalTable: "hospitals",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inpatient_bed_placements_departments",
                    columns: x => new { x.TenantId, x.HospitalId, x.DepartmentId },
                    principalTable: "departments",
                    principalColumns: new[] { "TenantId", "HospitalId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inpatient_bed_placements_wards",
                    columns: x => new { x.TenantId, x.DepartmentId, x.WardId },
                    principalTable: "wards",
                    principalColumns: new[] { "TenantId", "DepartmentId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inpatient_bed_placements_rooms",
                    columns: x => new { x.TenantId, x.WardId, x.RoomId },
                    principalTable: "rooms",
                    principalColumns: new[] { "TenantId", "WardId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_inpatient_bed_placements_beds",
                    columns: x => new { x.TenantId, x.RoomId, x.BedId },
                    principalTable: "beds",
                    principalColumns: new[] { "TenantId", "RoomId", "Id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_inpatient_admissions_TenantId_HospitalId_PatientId",
            table: "inpatient_admissions",
            columns: new[] { "TenantId", "HospitalId", "PatientId" },
            unique: true,
            filter: "\"Status\" = 'Active'");

        migrationBuilder.CreateIndex(
            name: "IX_inpatient_admissions_TenantId_HospitalId_Status_AdmittedAtUtc",
            table: "inpatient_admissions",
            columns: new[] { "TenantId", "HospitalId", "Status", "AdmittedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_inpatient_bed_placements_TenantId_AdmissionId",
            table: "inpatient_bed_placements",
            columns: new[] { "TenantId", "AdmissionId" },
            unique: true,
            filter: "\"EndedAtUtc\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_inpatient_bed_placements_TenantId_BedId",
            table: "inpatient_bed_placements",
            columns: new[] { "TenantId", "BedId" },
            unique: true,
            filter: "\"EndedAtUtc\" IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_inpatient_bed_placements_TenantId_HospitalId_StartedAtUtc",
            table: "inpatient_bed_placements",
            columns: new[] { "TenantId", "HospitalId", "StartedAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "inpatient_bed_placements");
        migrationBuilder.DropTable(name: "inpatient_admissions");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_beds_TenantId_RoomId_Id",
            table: "beds");
        migrationBuilder.DropUniqueConstraint(
            name: "AK_rooms_TenantId_WardId_Id",
            table: "rooms");
        migrationBuilder.DropUniqueConstraint(
            name: "AK_wards_TenantId_DepartmentId_Id",
            table: "wards");
    }
}
