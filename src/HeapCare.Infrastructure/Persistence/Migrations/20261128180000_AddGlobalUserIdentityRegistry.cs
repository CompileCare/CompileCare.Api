using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeapCare.Infrastructure.Persistence.Migrations;

[DbContext(typeof(HeapCareDbContext))]
[Migration("20261128180000_AddGlobalUserIdentityRegistry")]
public partial class AddGlobalUserIdentityRegistry : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        HeapCareDbContextModelSnapshot.BuildModelForMigration(modelBuilder);

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "user_identities",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Issuer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_identities", x => x.Id);
                table.UniqueConstraint(
                    "AK_user_identities_Id_Issuer_Subject",
                    x => new { x.Id, x.Issuer, x.Subject });
                table.CheckConstraint(
                    "CK_user_identities_identity",
                    "\"Issuer\" <> '' AND \"Subject\" <> ''");
            });

        migrationBuilder.CreateIndex(
            name: "IX_user_identities_Issuer_Subject",
            table: "user_identities",
            columns: new[] { "Issuer", "Subject" },
            unique: true);

        migrationBuilder.AddColumn<Guid>(
            name: "UserIdentityId",
            table: "tenant_memberships",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "UserIdentityId",
            table: "platform_operators",
            type: "uuid",
            nullable: true);

        migrationBuilder.Sql("""
            INSERT INTO "user_identities"
                ("Id", "Issuer", "Subject", "CreatedAtUtc", "UpdatedAtUtc", "Version")
            SELECT
                gen_random_uuid(), identities."Issuer", identities."Subject",
                NOW(), NOW(), gen_random_uuid()
            FROM (
                SELECT "Issuer", "Subject" FROM "tenant_memberships"
                UNION
                SELECT "Issuer", "Subject" FROM "platform_operators"
            ) AS identities
            WHERE identities."Issuer" <> '' AND identities."Subject" <> ''
            ON CONFLICT ("Issuer", "Subject") DO NOTHING;
            """);

        migrationBuilder.Sql("""
            UPDATE "tenant_memberships" AS membership
            SET "UserIdentityId" = identity."Id"
            FROM "user_identities" AS identity
            WHERE membership."Issuer" = identity."Issuer"
              AND membership."Subject" = identity."Subject";
            """);

        migrationBuilder.Sql("""
            UPDATE "platform_operators" AS platform_operator
            SET "UserIdentityId" = identity."Id"
            FROM "user_identities" AS identity
            WHERE platform_operator."Issuer" = identity."Issuer"
              AND platform_operator."Subject" = identity."Subject";
            """);

        migrationBuilder.AlterColumn<Guid>(
            name: "UserIdentityId",
            table: "tenant_memberships",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.AlterColumn<Guid>(
            name: "UserIdentityId",
            table: "platform_operators",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_tenant_memberships_UserIdentityId_Issuer_Subject",
            table: "tenant_memberships",
            columns: new[] { "UserIdentityId", "Issuer", "Subject" });

        migrationBuilder.CreateIndex(
            name: "IX_platform_operators_UserIdentityId_Issuer_Subject",
            table: "platform_operators",
            columns: new[] { "UserIdentityId", "Issuer", "Subject" });

        migrationBuilder.AddForeignKey(
            name: "FK_tenant_memberships_user_identities_UserIdentityId_Issuer_Subject",
            table: "tenant_memberships",
            columns: new[] { "UserIdentityId", "Issuer", "Subject" },
            principalTable: "user_identities",
            principalColumns: new[] { "Id", "Issuer", "Subject" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_platform_operators_user_identities_UserIdentityId_Issuer_Subject",
            table: "platform_operators",
            columns: new[] { "UserIdentityId", "Issuer", "Subject" },
            principalTable: "user_identities",
            principalColumns: new[] { "Id", "Issuer", "Subject" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_platform_operators_user_identities_UserIdentityId_Issuer_Subject",
            table: "platform_operators");
        migrationBuilder.DropForeignKey(
            name: "FK_tenant_memberships_user_identities_UserIdentityId_Issuer_Subject",
            table: "tenant_memberships");

        migrationBuilder.DropIndex(
            name: "IX_platform_operators_UserIdentityId_Issuer_Subject",
            table: "platform_operators");
        migrationBuilder.DropIndex(
            name: "IX_tenant_memberships_UserIdentityId_Issuer_Subject",
            table: "tenant_memberships");

        migrationBuilder.DropColumn(
            name: "UserIdentityId",
            table: "platform_operators");
        migrationBuilder.DropColumn(
            name: "UserIdentityId",
            table: "tenant_memberships");

        migrationBuilder.DropTable(name: "user_identities");
    }
}
