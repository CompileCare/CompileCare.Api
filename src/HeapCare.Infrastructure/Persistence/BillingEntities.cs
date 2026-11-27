namespace HeapCare.Infrastructure.Persistence;

public sealed class BillingServiceItem : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class BillingInvoice : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid QueueEntryId { get; set; }
    public Guid PatientId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTimeOffset IssuedAtUtc { get; set; }
    public string Status { get; set; } = "Issued";
    public decimal Subtotal { get; set; }
    public int PaymentSequence { get; set; }
}

public sealed class BillingInvoiceLine : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public Guid InvoiceId { get; set; }
    public Guid ServiceItemId { get; set; }
    public string ServiceCode { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class BillingPayment : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public Guid InvoiceId { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Method { get; set; } = string.Empty;
    public string? Reference { get; set; }
    public DateTimeOffset PaidAtUtc { get; set; }
}
