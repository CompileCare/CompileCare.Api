using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompileCare.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CompileCareDbContext))]
[Migration("20261124150000_AddFrontDeskQueue")]
public sealed class AddFrontDeskQueue : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "queue_entries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                AppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                QueueDateLocal = table.Column<DateOnly>(type: "date", nullable: false),
                TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TokenNumber = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CheckedInAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CalledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClosureReason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_queue_entries", x => x.Id);
                table.UniqueConstraint("AK_queue_entries_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_queue_entries_token_positive", "\"TokenNumber\" > 0");
                table.CheckConstraint("CK_queue_entries_status", "\"Status\" IN ('Waiting', 'Called', 'InProgress', 'Completed', 'Cancelled', 'NoShow')");
                table.CheckConstraint("CK_queue_entries_closure_reason", "\"ClosureReason\" IS NULL OR length(trim(\"ClosureReason\")) BETWEEN 1 AND 250");
                table.ForeignKey("FK_queue_entries_hospitals_TenantId_HospitalId", x => new { x.TenantId, x.HospitalId }, "hospitals", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_queue_entries_departments_TenantId_HospitalId_DepartmentId", x => new { x.TenantId, x.HospitalId, x.DepartmentId }, "departments", new[] { "TenantId", "HospitalId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_queue_entries_hospital_patient_registrations_TenantId_HospitalId_PatientId", x => new { x.TenantId, x.HospitalId, x.PatientId }, "hospital_patient_registrations", new[] { "TenantId", "HospitalId", "PatientId" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_queue_entries_appointments_TenantId_AppointmentId", x => new { x.TenantId, x.AppointmentId }, "appointments", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_queue_entries_tenants_TenantId", x => x.TenantId, "tenants", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_queue_entries_daily_token", "queue_entries",
            new[] { "TenantId", "HospitalId", "DepartmentId", "QueueDateLocal", "TokenNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_queue_entries_daily_status_token", "queue_entries",
            new[] { "TenantId", "HospitalId", "DepartmentId", "QueueDateLocal", "Status", "TokenNumber" });
        migrationBuilder.CreateIndex("IX_queue_entries_TenantId_AppointmentId", "queue_entries",
            new[] { "TenantId", "AppointmentId" }, unique: true, filter: "\"AppointmentId\" IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "queue_entries");

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        // The runtime model is represented by CompileCareDbContextModelSnapshot.
    }
}
