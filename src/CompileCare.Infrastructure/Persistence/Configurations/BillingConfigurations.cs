using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompileCare.Infrastructure.Persistence.Configurations;

public sealed class BillingServiceItemConfiguration : IEntityTypeConfiguration<BillingServiceItem>
{
    public void Configure(EntityTypeBuilder<BillingServiceItem> builder)
    {
        builder.ToTable("billing_service_items", table =>
        {
            table.HasCheckConstraint("CK_billing_service_items_price", "\"UnitPrice\" >= 0");
            table.HasCheckConstraint("CK_billing_service_items_code", "length(trim(\"Code\")) BETWEEN 1 AND 40");
            table.HasCheckConstraint("CK_billing_service_items_name", "length(trim(\"Name\")) BETWEEN 1 AND 160");
        });
        builder.Property(x => x.Code).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(80).IsRequired();
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasAlternateKey(x => new { x.TenantId, x.HospitalId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.IsActive, x.Category });
        builder.HasOne<Hospital>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BillingInvoiceConfiguration : IEntityTypeConfiguration<BillingInvoice>
{
    public void Configure(EntityTypeBuilder<BillingInvoice> builder)
    {
        builder.ToTable("billing_invoices", table =>
        {
            table.HasCheckConstraint("CK_billing_invoices_subtotal", "\"Subtotal\" >= 0");
            table.HasCheckConstraint("CK_billing_invoices_status", "\"Status\" IN ('Issued')");
            table.HasCheckConstraint("CK_billing_invoices_payment_sequence", "\"PaymentSequence\" >= 0");
        });
        builder.Property(x => x.InvoiceNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Subtotal).HasPrecision(18, 2);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasAlternateKey(x => new { x.TenantId, x.HospitalId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.InvoiceNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.QueueEntryId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.PatientId, x.IssuedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.DepartmentId });
        builder.HasOne<Hospital>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Department>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.DepartmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QueueEntry>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.QueueEntryId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<HospitalPatientRegistration>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.PatientId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.PatientId }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BillingInvoiceLineConfiguration : IEntityTypeConfiguration<BillingInvoiceLine>
{
    public void Configure(EntityTypeBuilder<BillingInvoiceLine> builder)
    {
        builder.ToTable("billing_invoice_lines", table =>
        {
            table.HasCheckConstraint("CK_billing_invoice_lines_quantity", "\"Quantity\" BETWEEN 1 AND 1000");
            table.HasCheckConstraint("CK_billing_invoice_lines_price", "\"UnitPrice\" >= 0 AND \"LineTotal\" >= 0");
        });
        builder.Property(x => x.ServiceCode).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(160).IsRequired();
        builder.Property(x => x.UnitPrice).HasPrecision(18, 2);
        builder.Property(x => x.LineTotal).HasPrecision(18, 2);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.InvoiceId });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId });
        builder.HasOne<BillingInvoice>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.InvoiceId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<BillingServiceItem>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.ServiceItemId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BillingPaymentConfiguration : IEntityTypeConfiguration<BillingPayment>
{
    public void Configure(EntityTypeBuilder<BillingPayment> builder)
    {
        builder.ToTable("billing_payments", table =>
        {
            table.HasCheckConstraint("CK_billing_payments_amount", "\"Amount\" > 0");
            table.HasCheckConstraint("CK_billing_payments_method", "\"Method\" IN ('Cash', 'UPI', 'Card', 'BankTransfer')");
        });
        builder.Property(x => x.ReceiptNumber).HasMaxLength(48).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Method).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(80);
        builder.HasAlternateKey(x => new { x.TenantId, x.Id });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.ReceiptNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.InvoiceId, x.PaidAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.HospitalId, x.PaidAtUtc });
        builder.HasOne<BillingInvoice>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId, x.InvoiceId })
            .HasPrincipalKey(x => new { x.TenantId, x.HospitalId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hospital>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
