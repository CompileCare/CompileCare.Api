using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HeapCare.Infrastructure.Persistence.Migrations;

[DbContext(typeof(HeapCareDbContext))]
[Migration("20261124190000_AddOpdBillingFoundation")]
public partial class AddOpdBillingFoundation : Migration
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        HeapCareDbContextModelSnapshot.BuildModelForMigration(modelBuilder);

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddUniqueConstraint(
            name: "AK_queue_entries_TenantId_HospitalId_Id",
            table: "queue_entries",
            columns: new[] { "TenantId", "HospitalId", "Id" });

        migrationBuilder.CreateTable(
            name: "billing_service_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_billing_service_items", x => x.Id);
                table.UniqueConstraint("AK_billing_service_items_TenantId_Id", x => new { x.TenantId, x.Id });
                table.UniqueConstraint("AK_billing_service_items_TenantId_HospitalId_Id", x => new { x.TenantId, x.HospitalId, x.Id });
                table.CheckConstraint("CK_billing_service_items_price", "\"UnitPrice\" >= 0");
                table.CheckConstraint("CK_billing_service_items_code", "length(trim(\"Code\")) BETWEEN 1 AND 40");
                table.CheckConstraint("CK_billing_service_items_name", "length(trim(\"Name\")) BETWEEN 1 AND 160");
                table.ForeignKey("FK_billing_service_items_hospitals_TenantId_HospitalId", x => new { x.TenantId, x.HospitalId }, "hospitals", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_billing_service_items_tenants_TenantId", x => x.TenantId, "tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "billing_invoices",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                DepartmentId = table.Column<Guid>(type: "uuid", nullable: false),
                QueueEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                InvoiceNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                IssuedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Subtotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                PaymentSequence = table.Column<int>(type: "integer", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_billing_invoices", x => x.Id);
                table.UniqueConstraint("AK_billing_invoices_TenantId_Id", x => new { x.TenantId, x.Id });
                table.UniqueConstraint("AK_billing_invoices_TenantId_HospitalId_Id", x => new { x.TenantId, x.HospitalId, x.Id });
                table.CheckConstraint("CK_billing_invoices_subtotal", "\"Subtotal\" >= 0");
                table.CheckConstraint("CK_billing_invoices_status", "\"Status\" IN ('Issued')");
                table.CheckConstraint("CK_billing_invoices_payment_sequence", "\"PaymentSequence\" >= 0");
                table.ForeignKey("FK_billing_invoices_departments_TenantId_HospitalId_DepartmentId", x => new { x.TenantId, x.HospitalId, x.DepartmentId }, "departments", new[] { "TenantId", "HospitalId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_billing_invoices_hospital_patient_registrations_TenantId_HospitalId_PatientId", x => new { x.TenantId, x.HospitalId, x.PatientId }, "hospital_patient_registrations", new[] { "TenantId", "HospitalId", "PatientId" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_billing_invoices_hospitals_TenantId_HospitalId", x => new { x.TenantId, x.HospitalId }, "hospitals", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_billing_invoices_queue_entries_TenantId_HospitalId_QueueEntryId", x => new { x.TenantId, x.HospitalId, x.QueueEntryId }, "queue_entries", new[] { "TenantId", "HospitalId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_billing_invoices_tenants_TenantId", x => x.TenantId, "tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "billing_invoice_lines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                ServiceItemId = table.Column<Guid>(type: "uuid", nullable: false),
                ServiceCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Description = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                UnitPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                LineTotal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_billing_invoice_lines", x => x.Id);
                table.UniqueConstraint("AK_billing_invoice_lines_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_billing_invoice_lines_quantity", "\"Quantity\" BETWEEN 1 AND 1000");
                table.CheckConstraint("CK_billing_invoice_lines_price", "\"UnitPrice\" >= 0 AND \"LineTotal\" >= 0");
                table.ForeignKey("FK_billing_invoice_lines_billing_invoices_TenantId_HospitalId_InvoiceId", x => new { x.TenantId, x.HospitalId, x.InvoiceId }, "billing_invoices", new[] { "TenantId", "HospitalId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_billing_invoice_lines_billing_service_items_TenantId_HospitalId_ServiceItemId", x => new { x.TenantId, x.HospitalId, x.ServiceItemId }, "billing_service_items", new[] { "TenantId", "HospitalId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_billing_invoice_lines_tenants_TenantId", x => x.TenantId, "tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "billing_payments",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                HospitalId = table.Column<Guid>(type: "uuid", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                ReceiptNumber = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Method = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Reference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                PaidAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_billing_payments", x => x.Id);
                table.UniqueConstraint("AK_billing_payments_TenantId_Id", x => new { x.TenantId, x.Id });
                table.CheckConstraint("CK_billing_payments_amount", "\"Amount\" > 0");
                table.CheckConstraint("CK_billing_payments_method", "\"Method\" IN ('Cash', 'UPI', 'Card', 'BankTransfer')");
                table.ForeignKey("FK_billing_payments_billing_invoices_TenantId_HospitalId_InvoiceId", x => new { x.TenantId, x.HospitalId, x.InvoiceId }, "billing_invoices", new[] { "TenantId", "HospitalId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_billing_payments_hospitals_TenantId_HospitalId", x => new { x.TenantId, x.HospitalId }, "hospitals", new[] { "TenantId", "Id" }, onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_billing_payments_tenants_TenantId", x => x.TenantId, "tenants", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_billing_service_items_TenantId_HospitalId_IsActive_Category", "billing_service_items", new[] { "TenantId", "HospitalId", "IsActive", "Category" });
        migrationBuilder.CreateIndex("IX_billing_service_items_TenantId_HospitalId_Code", "billing_service_items", new[] { "TenantId", "HospitalId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_billing_invoices_TenantId_HospitalId_PatientId_IssuedAtUtc", "billing_invoices", new[] { "TenantId", "HospitalId", "PatientId", "IssuedAtUtc" });
        migrationBuilder.CreateIndex("IX_billing_invoices_TenantId_HospitalId_DepartmentId", "billing_invoices", new[] { "TenantId", "HospitalId", "DepartmentId" });
        migrationBuilder.CreateIndex("IX_billing_invoices_TenantId_HospitalId_InvoiceNumber", "billing_invoices", new[] { "TenantId", "HospitalId", "InvoiceNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_billing_invoices_TenantId_HospitalId_QueueEntryId", "billing_invoices", new[] { "TenantId", "HospitalId", "QueueEntryId" }, unique: true);
        migrationBuilder.CreateIndex("IX_billing_invoice_lines_TenantId_HospitalId", "billing_invoice_lines", new[] { "TenantId", "HospitalId" });
        migrationBuilder.CreateIndex("IX_billing_invoice_lines_TenantId_InvoiceId", "billing_invoice_lines", new[] { "TenantId", "InvoiceId" });
        migrationBuilder.CreateIndex("IX_billing_invoice_lines_TenantId_HospitalId_InvoiceId", "billing_invoice_lines", new[] { "TenantId", "HospitalId", "InvoiceId" });
        migrationBuilder.CreateIndex("IX_billing_invoice_lines_TenantId_HospitalId_ServiceItemId", "billing_invoice_lines", new[] { "TenantId", "HospitalId", "ServiceItemId" });
        migrationBuilder.CreateIndex("IX_billing_payments_TenantId_HospitalId_PaidAtUtc", "billing_payments", new[] { "TenantId", "HospitalId", "PaidAtUtc" });
        migrationBuilder.CreateIndex("IX_billing_payments_TenantId_HospitalId_ReceiptNumber", "billing_payments", new[] { "TenantId", "HospitalId", "ReceiptNumber" }, unique: true);
        migrationBuilder.CreateIndex("IX_billing_payments_TenantId_InvoiceId_PaidAtUtc", "billing_payments", new[] { "TenantId", "InvoiceId", "PaidAtUtc" });
        migrationBuilder.CreateIndex("IX_billing_payments_TenantId_HospitalId_InvoiceId", "billing_payments", new[] { "TenantId", "HospitalId", "InvoiceId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "billing_invoice_lines");
        migrationBuilder.DropTable(name: "billing_payments");
        migrationBuilder.DropTable(name: "billing_invoices");
        migrationBuilder.DropTable(name: "billing_service_items");
        migrationBuilder.DropUniqueConstraint(name: "AK_queue_entries_TenantId_HospitalId_Id", table: "queue_entries");
    }
}
