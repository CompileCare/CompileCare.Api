using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompileCare.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFacilityHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hospitals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hospitals", x => x.Id);
                    table.UniqueConstraint("AK_hospitals_TenantId_Id", x => new { x.TenantId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_departments", x => x.Id);
                    table.UniqueConstraint("AK_departments_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_departments_hospitals_TenantId_HospitalId",
                        columns: x => new { x.TenantId, x.HospitalId },
                        principalTable: "hospitals",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wards", x => x.Id);
                    table.UniqueConstraint("AK_wards_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_wards_departments_TenantId_DepartmentId",
                        columns: x => new { x.TenantId, x.DepartmentId },
                        principalTable: "departments",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WardId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rooms", x => x.Id);
                    table.UniqueConstraint("AK_rooms_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_rooms_wards_TenantId_WardId",
                        columns: x => new { x.TenantId, x.WardId },
                        principalTable: "wards",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "beds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_beds", x => x.Id);
                    table.UniqueConstraint("AK_beds_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_beds_rooms_TenantId_RoomId",
                        columns: x => new { x.TenantId, x.RoomId },
                        principalTable: "rooms",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_beds_TenantId_RoomId_Number",
                table: "beds",
                columns: new[] { "TenantId", "RoomId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_departments_TenantId_HospitalId_Code",
                table: "departments",
                columns: new[] { "TenantId", "HospitalId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_departments_TenantId_HospitalId_Name",
                table: "departments",
                columns: new[] { "TenantId", "HospitalId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_hospitals_TenantId_Code",
                table: "hospitals",
                columns: new[] { "TenantId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_hospitals_TenantId_Name",
                table: "hospitals",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_rooms_TenantId_WardId_Name",
                table: "rooms",
                columns: new[] { "TenantId", "WardId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wards_TenantId_DepartmentId_Name",
                table: "wards",
                columns: new[] { "TenantId", "DepartmentId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "beds");

            migrationBuilder.DropTable(
                name: "rooms");

            migrationBuilder.DropTable(
                name: "wards");

            migrationBuilder.DropTable(
                name: "departments");

            migrationBuilder.DropTable(
                name: "hospitals");
        }
    }
}
