namespace CompileCare.Application.Billing;

public static class BillingPermissions
{
    public const string CatalogueManage = "billing.catalogue.manage";
    public const string InvoiceRead = "billing.invoice.read";
    public const string InvoiceManage = "billing.invoice.manage";
    public const string PaymentManage = "billing.payment.manage";
    public const string ReportRead = "billing.report.read";
}

public static class PaymentMethods
{
    public const string Cash = "Cash";
    public const string Upi = "UPI";
    public const string Card = "Card";
    public const string BankTransfer = "BankTransfer";

    public static bool IsSupported(string value) =>
        value is Cash or Upi or Card or BankTransfer;
}

public static class InvoiceStatuses
{
    public const string Issued = "Issued";
    public const string PartiallyPaid = "PartiallyPaid";
    public const string Paid = "Paid";
}

public sealed record BillingServiceItemInput(string Code, string Name, string Category, decimal UnitPrice, bool IsActive = true);
public sealed record BillingServiceItemDto(Guid Id, Guid HospitalId, string Code, string Name, string Category, decimal UnitPrice, bool IsActive);
public sealed record InvoiceLineInput(Guid ServiceItemId, int Quantity);
public sealed record InvoiceCreateInput(Guid QueueEntryId, IReadOnlyList<InvoiceLineInput> Lines);
public sealed record InvoiceLineDto(Guid Id, string ServiceCode, string Description, int Quantity, decimal UnitPrice, decimal LineTotal);
public sealed record InvoiceDto(
    Guid Id, Guid HospitalId, Guid QueueEntryId, Guid PatientId, string InvoiceNumber,
    DateTimeOffset IssuedAtUtc, string Status, decimal Subtotal, decimal AmountPaid,
    decimal BalanceDue, IReadOnlyList<InvoiceLineDto> Lines);
public sealed record PaymentCreateInput(decimal Amount, string Method, string? Reference);
public sealed record PaymentDto(Guid Id, Guid InvoiceId, string ReceiptNumber, decimal Amount, string Method, string? Reference, DateTimeOffset PaidAtUtc);
public sealed record ReceiptDto(string ReceiptNumber, string InvoiceNumber, Guid HospitalId, Guid PatientId, decimal Amount, string Method, string? Reference, DateTimeOffset PaidAtUtc);
public sealed record DailyCollectionReportDto(DateOnly BusinessDate, string TimeZoneId, decimal TotalCollected, IReadOnlyDictionary<string, decimal> ByMethod, int PaymentCount);
public sealed record BillingResult<T>(T? Value, string? Error, int StatusCode)
{
    public bool IsSuccess => Error is null;
    public static BillingResult<T> Success(T value) => new(value, null, 200);
    public static BillingResult<T> Failure(string error, int statusCode) => new(default, error, statusCode);
}

public interface IBillingService
{
    Task<BillingResult<BillingServiceItemDto>> CreateServiceItemAsync(Guid hospitalId, BillingServiceItemInput input, CancellationToken ct);
    Task<BillingResult<BillingServiceItemDto>> UpdateServiceItemAsync(Guid hospitalId, Guid serviceItemId, BillingServiceItemInput input, CancellationToken ct);
    Task<BillingResult<IReadOnlyList<BillingServiceItemDto>>> ListServiceItemsAsync(Guid hospitalId, bool includeInactive, CancellationToken ct);
    Task<BillingResult<InvoiceDto>> CreateInvoiceAsync(Guid hospitalId, InvoiceCreateInput input, CancellationToken ct);
    Task<BillingResult<InvoiceDto>> GetInvoiceAsync(Guid hospitalId, Guid invoiceId, CancellationToken ct);
    Task<BillingResult<PaymentDto>> RecordPaymentAsync(Guid hospitalId, Guid invoiceId, PaymentCreateInput input, CancellationToken ct);
    Task<BillingResult<ReceiptDto>> GetReceiptAsync(Guid hospitalId, Guid paymentId, CancellationToken ct);
    Task<BillingResult<DailyCollectionReportDto>> GetDailyCollectionAsync(Guid hospitalId, DateOnly businessDate, string timeZoneId, CancellationToken ct);
}
