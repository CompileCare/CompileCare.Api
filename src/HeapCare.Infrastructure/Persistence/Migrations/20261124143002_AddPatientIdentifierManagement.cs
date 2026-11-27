using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeapCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientIdentifierManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_hospital_patient_registrations_TenantId_HospitalId_PatientId",
                table: "hospital_patient_registrations");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_hospital_patient_registrations_TenantId_HospitalId_PatientId",
                table: "hospital_patient_registrations",
                columns: new[] { "TenantId", "HospitalId", "PatientId" });

            migrationBuilder.CreateTable(
                name: "hospital_patient_identifier_policies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NumberLength = table.Column<int>(type: "integer", nullable: false),
                    NextNumber = table.Column<long>(type: "bigint", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hospital_patient_identifier_policies", x => x.Id);
                    table.UniqueConstraint("AK_hospital_patient_identifier_policies_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_hospital_patient_identifier_policies_next_number", "\"NextNumber\" > 0");
                    table.CheckConstraint("CK_hospital_patient_identifier_policies_number_length", "\"NumberLength\" BETWEEN 1 AND 12");
                    table.CheckConstraint("CK_hospital_patient_identifier_policies_prefix", "\"Prefix\" ~ '^[A-Z0-9][A-Z0-9-]{0,19}$'");
                    table.ForeignKey(
                        name: "FK_hospital_patient_identifier_policies_hospitals_TenantId_Hos~",
                        columns: x => new { x.TenantId, x.HospitalId },
                        principalTable: "hospitals",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_hospital_patient_identifier_policies_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "patient_identifiers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    TypeCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NormalizedValue = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_patient_identifiers", x => x.Id);
                    table.UniqueConstraint("AK_patient_identifiers_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.CheckConstraint("CK_patient_identifiers_normalized_value", "\"NormalizedValue\" <> '' AND \"NormalizedValue\" = upper(btrim(\"NormalizedValue\"))");
                    table.CheckConstraint("CK_patient_identifiers_type_code", "\"TypeCode\" = 'MRN'");
                    table.ForeignKey(
                        name: "FK_patient_identifiers_hospital_patient_registrations_TenantId~",
                        columns: x => new { x.TenantId, x.HospitalId, x.PatientId },
                        principalTable: "hospital_patient_registrations",
                        principalColumns: new[] { "TenantId", "HospitalId", "PatientId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_patient_identifiers_tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hospital_patient_identifier_policies_TenantId_HospitalId",
                table: "hospital_patient_identifier_policies",
                columns: new[] { "TenantId", "HospitalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_identifiers_TenantId_HospitalId_PatientId_TypeCode",
                table: "patient_identifiers",
                columns: new[] { "TenantId", "HospitalId", "PatientId", "TypeCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_patient_identifiers_TenantId_HospitalId_TypeCode_Normalized~",
                table: "patient_identifiers",
                columns: new[] { "TenantId", "HospitalId", "TypeCode", "NormalizedValue" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hospital_patient_identifier_policies");

            migrationBuilder.DropTable(
                name: "patient_identifiers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_hospital_patient_registrations_TenantId_HospitalId_PatientId",
                table: "hospital_patient_registrations");

            migrationBuilder.CreateIndex(
                name: "IX_hospital_patient_registrations_TenantId_HospitalId_PatientId",
                table: "hospital_patient_registrations",
                columns: new[] { "TenantId", "HospitalId", "PatientId" },
                unique: true);
        }
    }
}
