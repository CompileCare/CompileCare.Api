using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompileCare.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CompileCareDbContext))]
[Migration("20261124170000_AddOpdEncounters")]
public sealed class AddOpdEncounters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "opd_encounters",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                QueueEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ChiefComplaint = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                History = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                Examination = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                Assessment = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Plan = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                CreatedByIssuer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CreatedBySubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                SignedByIssuer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SignedBySubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CancellationReason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_opd_encounters", x => x.Id);
                table.UniqueConstraint("AK_opd_encounters_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_opd_encounters_status", "\"Status\" IN ('InProgress', 'Signed', 'Closed', 'Cancelled')");
                table.CheckConstraint("CK_opd_encounters_signed_attribution", "\"Status\" NOT IN ('Signed', 'Closed') OR (\"SignedAtUtc\" IS NOT NULL AND \"SignedByIssuer\" IS NOT NULL AND \"SignedBySubject\" IS NOT NULL)");
                table.CheckConstraint("CK_opd_encounters_cancel_reason", "\"Status\" <> 'Cancelled' OR (\"CancellationReason\" IS NOT NULL AND length(trim(\"CancellationReason\")) BETWEEN 1 AND 250)");
                table.ForeignKey("FK_opd_encounters_queue_entries_TenantId_QueueEntryId", x => new { x.TenantId, x.QueueEntryId }, "queue_entries", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_opd_encounters_hospitals_TenantId_HospitalId", x => new { x.TenantId, x.HospitalId }, "hospitals", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_opd_encounters_departments_TenantId_HospitalId_DepartmentId", x => new { x.TenantId, x.HospitalId, x.DepartmentId }, "departments", new[] { "TenantId", "HospitalId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_opd_encounters_hospital_patient_registrations_TenantId_HospitalId_PatientId", x => new { x.TenantId, x.HospitalId, x.PatientId }, "hospital_patient_registrations", new[] { "TenantId", "HospitalId", "PatientId" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_opd_encounters_tenants_TenantId", x => x.TenantId, "tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "opd_encounter_amendments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EncounterId = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                Reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                Content = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                AuthorIssuer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                AuthorSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_opd_encounter_amendments", x => x.Id);
                table.UniqueConstraint("AK_opd_encounter_amendments_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_opd_encounter_amendments_reason", "length(trim(\"Reason\")) BETWEEN 1 AND 250");
                table.CheckConstraint("CK_opd_encounter_amendments_content", "length(trim(\"Content\")) BETWEEN 1 AND 4000");
                table.ForeignKey("FK_opd_encounter_amendments_encounters_TenantId_EncounterId", x => new { x.TenantId, x.EncounterId }, "opd_encounters", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_opd_encounter_amendments_hospitals_TenantId_HospitalId", x => new { x.TenantId, x.HospitalId }, "hospitals", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_opd_encounter_amendments_departments_TenantId_HospitalId_DepartmentId", x => new { x.TenantId, x.HospitalId, x.DepartmentId }, "departments", new[] { "TenantId", "HospitalId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_opd_encounter_amendments_tenants_TenantId", x => x.TenantId, "tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_opd_encounters_TenantId_QueueEntryId",
            table: "opd_encounters",
            columns: new[] { "TenantId", "QueueEntryId" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_opd_encounters_TenantId_HospitalId_PatientId_StartedAtUtc",
            table: "opd_encounters",
            columns: new[] { "TenantId", "HospitalId", "PatientId", "StartedAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_opd_encounters_TenantId_HospitalId_DepartmentId",
            table: "opd_encounters",
            columns: new[] { "TenantId", "HospitalId", "DepartmentId" });
        migrationBuilder.CreateIndex(
            name: "IX_opd_encounter_amendments_TenantId_EncounterId_CreatedAtUtc",
            table: "opd_encounter_amendments",
            columns: new[] { "TenantId", "EncounterId", "CreatedAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_opd_encounter_amendments_TenantId_HospitalId_DepartmentId",
            table: "opd_encounter_amendments",
            columns: new[] { "TenantId", "HospitalId", "DepartmentId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "opd_encounter_amendments");
        migrationBuilder.DropTable(name: "opd_encounters");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        // The runtime model is represented by CompileCareDbContextModelSnapshot.
    }
}
