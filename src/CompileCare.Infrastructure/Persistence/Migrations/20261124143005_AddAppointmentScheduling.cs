using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompileCare.Infrastructure.Persistence.Migrations;

public partial class AddAppointmentScheduling : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "provider_availability_sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                ProviderStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                StartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                EndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                SlotDurationMinutes = table.Column<int>(type: "integer", nullable: false),
                TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_provider_availability_sessions", x => x.Id);
                table.UniqueConstraint("AK_provider_availability_sessions_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_provider_availability_sessions_time_range", "\"EndsAtUtc\" > \"StartsAtUtc\"");
                table.CheckConstraint("CK_provider_availability_sessions_slot_duration", "\"SlotDurationMinutes\" BETWEEN 5 AND 120");
                table.ForeignKey(
                    name: "FK_provider_availability_sessions_departments_TenantId_HospitalId_DepartmentId",
                    columns: x => new { x.TenantId, x.HospitalId, x.DepartmentId },
                    principalTable: "departments",
                    principalColumns: new[] { "TenantId", "HospitalId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_provider_availability_sessions_hospitals_TenantId_HospitalId",
                    columns: x => new { x.TenantId, x.HospitalId },
                    principalTable: "hospitals",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_provider_availability_sessions_staff_profiles_TenantId_ProviderStaffId",
                    columns: x => new { x.TenantId, x.ProviderStaffId },
                    principalTable: "staff_profiles",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_provider_availability_sessions_tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "appointments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                ProviderStaffId = table.Column<Guid>(type: "uuid", nullable: false),
                AvailabilitySessionId = table.Column<Guid>(type: "uuid", nullable: false),
                SlotStartsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                SlotEndsAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_appointments", x => x.Id);
                table.UniqueConstraint("AK_appointments_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_appointments_time_range", "\"SlotEndsAtUtc\" > \"SlotStartsAtUtc\"");
                table.CheckConstraint("CK_appointments_status", "\"Status\" IN ('Booked', 'Cancelled', 'Completed', 'NoShow')");
                table.ForeignKey(
                    name: "FK_appointments_departments_TenantId_HospitalId_DepartmentId",
                    columns: x => new { x.TenantId, x.HospitalId, x.DepartmentId },
                    principalTable: "departments",
                    principalColumns: new[] { "TenantId", "HospitalId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_appointments_hospital_patient_registrations_TenantId_HospitalId_PatientId",
                    columns: x => new { x.TenantId, x.HospitalId, x.PatientId },
                    principalTable: "hospital_patient_registrations",
                    principalColumns: new[] { "TenantId", "HospitalId", "PatientId" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_appointments_hospitals_TenantId_HospitalId",
                    columns: x => new { x.TenantId, x.HospitalId },
                    principalTable: "hospitals",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_appointments_provider_availability_sessions_TenantId_AvailabilitySessionId",
                    columns: x => new { x.TenantId, x.AvailabilitySessionId },
                    principalTable: "provider_availability_sessions",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_appointments_staff_profiles_TenantId_ProviderStaffId",
                    columns: x => new { x.TenantId, x.ProviderStaffId },
                    principalTable: "staff_profiles",
                    principalColumns: new[] { "TenantId", "Id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_appointments_tenants_TenantId",
                    column: x => x.TenantId,
                    principalTable: "tenants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_provider_availability_sessions_TenantId_HospitalId_DepartmentId_LocalDate",
            table: "provider_availability_sessions",
            columns: new[] { "TenantId", "HospitalId", "DepartmentId", "LocalDate" });
        migrationBuilder.CreateIndex(
            name: "IX_provider_availability_sessions_TenantId_ProviderStaffId_LocalDate_StartsAtUtc_EndsAtUtc",
            table: "provider_availability_sessions",
            columns: new[] { "TenantId", "ProviderStaffId", "LocalDate", "StartsAtUtc", "EndsAtUtc" });
        migrationBuilder.CreateIndex(
            name: "IX_appointments_TenantId_AvailabilitySessionId_SlotStartsAtUtc",
            table: "appointments",
            columns: new[] { "TenantId", "AvailabilitySessionId", "SlotStartsAtUtc" },
            unique: true,
            filter: "\"Status\" = 'Booked'");
        migrationBuilder.CreateIndex(
            name: "IX_appointments_TenantId_PatientId_SlotStartsAtUtc",
            table: "appointments",
            columns: new[] { "TenantId", "PatientId", "SlotStartsAtUtc" },
            filter: "\"Status\" = 'Booked'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "appointments");
        migrationBuilder.DropTable(name: "provider_availability_sessions");
    }
}
