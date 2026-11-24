using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompileCare.Infrastructure.Persistence.Migrations;

public partial class AddPatientNameNormalizationAndMatching : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "NormalizedGivenName",
            table: "patients",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedMiddleName",
            table: "patients",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "NormalizedFamilyName",
            table: "patients",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "patients"
            SET "NormalizedGivenName" =
                    upper(btrim(regexp_replace(normalize("GivenName", NFKC), '[[:space:]]+', ' ', 'g'))),
                "NormalizedMiddleName" =
                    CASE WHEN "MiddleName" IS NULL OR btrim("MiddleName") = '' THEN NULL
                         ELSE upper(btrim(regexp_replace(normalize("MiddleName", NFKC), '[[:space:]]+', ' ', 'g')))
                    END,
                "NormalizedFamilyName" =
                    CASE WHEN "FamilyName" IS NULL OR btrim("FamilyName") = '' THEN NULL
                         ELSE upper(btrim(regexp_replace(normalize("FamilyName", NFKC), '[[:space:]]+', ' ', 'g')))
                    END;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "NormalizedGivenName",
            table: "patients",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(100)",
            oldMaxLength: 100,
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_patients_normalized_given_name",
            table: "patients",
            sql: "\"NormalizedGivenName\" <> ''");

        migrationBuilder.CreateIndex(
            name: "IX_patients_TenantId_DateOfBirth_NormalizedGivenName",
            table: "patients",
            columns: new[] { "TenantId", "DateOfBirth", "NormalizedGivenName" });

        migrationBuilder.CreateIndex(
            name: "IX_patients_TenantId_DateOfBirth_NormalizedMiddleName_NormalizedFamilyName",
            table: "patients",
            columns: new[] { "TenantId", "DateOfBirth", "NormalizedMiddleName", "NormalizedFamilyName" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_patients_TenantId_DateOfBirth_NormalizedGivenName",
            table: "patients");

        migrationBuilder.DropIndex(
            name: "IX_patients_TenantId_DateOfBirth_NormalizedMiddleName_NormalizedFamilyName",
            table: "patients");

        migrationBuilder.DropCheckConstraint(
            name: "CK_patients_normalized_given_name",
            table: "patients");

        migrationBuilder.DropColumn(name: "NormalizedFamilyName", table: "patients");
        migrationBuilder.DropColumn(name: "NormalizedGivenName", table: "patients");
        migrationBuilder.DropColumn(name: "NormalizedMiddleName", table: "patients");
    }
}
