using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompileCare.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CompileCareDbContext))]
[Migration("20261124160000_AddAppointmentRescheduleLineage")]
public sealed class AddAppointmentRescheduleLineage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "RescheduledFromAppointmentId",
            table: "appointments",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_appointments_TenantId_RescheduledFromAppointmentId",
            table: "appointments",
            columns: new[] { "TenantId", "RescheduledFromAppointmentId" });

        migrationBuilder.AddForeignKey(
            name: "FK_appointments_rescheduled_from",
            table: "appointments",
            columns: new[] { "TenantId", "RescheduledFromAppointmentId" },
            principalTable: "appointments",
            principalColumns: new[] { "TenantId", "Id" },
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_appointments_rescheduled_from",
            table: "appointments");

        migrationBuilder.DropIndex(
            name: "IX_appointments_TenantId_RescheduledFromAppointmentId",
            table: "appointments");

        migrationBuilder.DropColumn(
            name: "RescheduledFromAppointmentId",
            table: "appointments");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        // The runtime model is represented by CompileCareDbContextModelSnapshot.
    }
}
