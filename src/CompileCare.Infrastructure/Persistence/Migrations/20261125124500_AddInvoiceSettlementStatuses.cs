using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CompileCare.Infrastructure.Persistence.Migrations;

[DbContext(typeof(CompileCareDbContext))]
[Migration("20261125124500_AddInvoiceSettlementStatuses")]
public partial class AddInvoiceSettlementStatuses : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        CompileCareDbContextModelSnapshot.BuildModelForMigration(modelBuilder);

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_billing_invoices_status",
            table: "billing_invoices");
        migrationBuilder.AddCheckConstraint(
            name: "CK_billing_invoices_status",
            table: "billing_invoices",
            sql: "\\\"Status\\\" IN ('Issued', 'PartiallyPaid', 'Paid')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "CK_billing_invoices_status",
            table: "billing_invoices");
        migrationBuilder.AddCheckConstraint(
            name: "CK_billing_invoices_status",
            table: "billing_invoices",
            sql: "\\\"Status\\\" IN ('Issued')");
    }
}
