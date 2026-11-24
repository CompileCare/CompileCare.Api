using CompileCare.Application.Billing;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Billing;

public sealed class BillingService(
    CompileCareDbContext db,
    IOrganizationScopeContext organizationScope) : IBillingService
{
    public async Task<BillingResult<BillingServiceItemDto>> CreateServiceItemAsync(
        Guid hospitalId, BillingServiceItemInput input, CancellationToken ct)
    {
        if (!organizationScope.CanManageHospital(hospitalId))
            return BillingResult<BillingServiceItemDto>.Failure("Hospital not found.", 404);
        var validation = ValidateServiceItem(input);
        if (validation is not null) return BillingResult<BillingServiceItemDto>.Failure(validation, 400);
        var code = input.Code.Trim().ToUpperInvariant();
        if (await db.BillingServiceItems.AnyAsync(x => x.HospitalId == hospitalId && x.Code == code, ct))
            return BillingResult<BillingServiceItemDto>.Failure("A service with this code already exists at the hospital.", 409);

        var item = new BillingServiceItem
        {
            HospitalId = hospitalId, Code = code, Name = input.Name.Trim(),
            Category = input.Category.Trim(), UnitPrice = decimal.Round(input.UnitPrice, 2),
            IsActive = true
        };
        db.BillingServiceItems.Add(item);
        await db.SaveChangesAsync(ct);
        return BillingResult<BillingServiceItemDto>.Success(ToDto(item));
    }

    public async Task<BillingResult<BillingServiceItemDto>> UpdateServiceItemAsync(
        Guid hospitalId, Guid serviceItemId, BillingServiceItemInput input, CancellationToken ct)
    {
        if (!organizationScope.CanManageHospital(hospitalId))
            return BillingResult<BillingServiceItemDto>.Failure("Service item not found.", 404);
        var validation = ValidateServiceItem(input);
        if (validation is not null) return BillingResult<BillingServiceItemDto>.Failure(validation, 400);
        var item = await db.BillingServiceItems.FirstOrDefaultAsync(
            x => x.Id == serviceItemId && x.HospitalId == hospitalId, ct);
        if (item is null) return BillingResult<BillingServiceItemDto>.Failure("Service item not found.", 404);
        var code = input.Code.Trim().ToUpperInvariant();
        if (await db.BillingServiceItems.AnyAsync(
            x => x.HospitalId == hospitalId && x.Code == code && x.Id != serviceItemId, ct))
            return BillingResult<BillingServiceItemDto>.Failure("A service with this code already exists at the hospital.", 409);
        item.Code = code;
        item.Name = input.Name.Trim();
        item.Category = input.Category.Trim();
        item.UnitPrice = decimal.Round(input.UnitPrice, 2);
        await db.SaveChangesAsync(ct);
        return BillingResult<BillingServiceItemDto>.Success(ToDto(item));
    }

    public async Task<BillingResult<IReadOnlyList<BillingServiceItemDto>>> ListServiceItemsAsync(
        Guid hospitalId, bool includeInactive, CancellationToken ct)
    {
        if (!organizationScope.CanAccessHospital(hospitalId))
            return BillingResult<IReadOnlyList<BillingServiceItemDto>>.Failure("Hospital not found.", 404);
        var query = db.BillingServiceItems.AsNoTracking().Where(x => x.HospitalId == hospitalId);
        if (!includeInactive) query = query.Where(x => x.IsActive);
        var items = await query.OrderBy(x => x.Category).ThenBy(x => x.Name)
            .Select(x => new BillingServiceItemDto(x.Id, x.HospitalId, x.Code, x.Name, x.Category, x.UnitPrice, x.IsActive))
            .ToListAsync(ct);
        return BillingResult<IReadOnlyList<BillingServiceItemDto>>.Success(items);
    }

    public async Task<BillingResult<InvoiceDto>> CreateInvoiceAsync(
        Guid hospitalId, InvoiceCreateInput input, CancellationToken ct)
    {
        if (input.QueueEntryId == Guid.Empty || input.Lines is null || input.Lines.Count is < 1 or > 50)
            return BillingResult<InvoiceDto>.Failure("A queue visit and between 1 and 50 invoice lines are required.", 400);
        var queue = await db.QueueEntries.FirstOrDefaultAsync(
            x => x.Id == input.QueueEntryId && x.HospitalId == hospitalId, ct);
        if (queue is null || !organizationScope.CanManageDepartment(hospitalId, queue.DepartmentId))
            return BillingResult<InvoiceDto>.Failure("Queue visit not found.", 404);
        if (queue.Status is not ("InProgress" or "Completed"))
            return BillingResult<InvoiceDto>.Failure("An invoice can only be issued for an in-progress or completed OPD visit.", 409);
        if (await db.BillingInvoices.AnyAsync(x => x.QueueEntryId == queue.Id, ct))
            return BillingResult<InvoiceDto>.Failure("An invoice has already been issued for this visit.", 409);

        if (input.Lines.Any(x => x.ServiceItemId == Guid.Empty || x.Quantity is < 1 or > 1000) ||
            input.Lines.Select(x => x.ServiceItemId).Distinct().Count() != input.Lines.Count)
            return BillingResult<InvoiceDto>.Failure("Each invoice line must reference a unique service and quantity between 1 and 1000.", 400);

        var ids = input.Lines.Select(x => x.ServiceItemId).ToArray();
        var services = await db.BillingServiceItems
            .Where(x => x.HospitalId == hospitalId && x.IsActive && ids.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, ct);
        if (services.Count != ids.Length)
            return BillingResult<InvoiceDto>.Failure("One or more services are unavailable at this hospital.", 409);

        var invoice = new BillingInvoice
        {
            HospitalId = hospitalId, DepartmentId = queue.DepartmentId,
            QueueEntryId = queue.Id, PatientId = queue.PatientId,
            InvoiceNumber = string.Empty, IssuedAtUtc = DateTimeOffset.UtcNow,
            Status = "Issued"
        };
        invoice.InvoiceNumber = $"INV-{invoice.IssuedAtUtc:yyyyMMdd}-{invoice.Id.ToString("N")[..8].ToUpperInvariant()}";
        var lines = input.Lines.Select(line =>
        {
            var service = services[line.ServiceItemId];
            var lineTotal = decimal.Round(service.UnitPrice * line.Quantity, 2, MidpointRounding.AwayFromZero);
            return new BillingInvoiceLine
            {
                HospitalId = hospitalId, InvoiceId = invoice.Id, ServiceItemId = service.Id,
                ServiceCode = service.Code, Description = service.Name,
                Quantity = line.Quantity, UnitPrice = service.UnitPrice, LineTotal = lineTotal
            };
        }).ToArray();
        invoice.Subtotal = lines.Sum(x => x.LineTotal);
        db.BillingInvoices.Add(invoice);
        db.BillingInvoiceLines.AddRange(lines);
        await db.SaveChangesAsync(ct);
        return BillingResult<InvoiceDto>.Success(new(
            invoice.Id, invoice.HospitalId, invoice.QueueEntryId, invoice.PatientId,
            invoice.InvoiceNumber, invoice.IssuedAtUtc, invoice.Status, invoice.Subtotal,
            0m, invoice.Subtotal, lines.Select(ToDto).ToArray()));
    }

    public async Task<BillingResult<InvoiceDto>> GetInvoiceAsync(
        Guid hospitalId, Guid invoiceId, CancellationToken ct)
    {
        var invoice = await db.BillingInvoices.AsNoTracking()
            .Include(x => db.BillingInvoiceLines.Where(line => line.InvoiceId == x.Id))
            .FirstOrDefaultAsync(x => x.Id == invoiceId && x.HospitalId == hospitalId, ct);
        if (invoice is null || !organizationScope.CanManageDepartment(hospitalId, invoice.DepartmentId))
            return BillingResult<InvoiceDto>.Failure("Invoice not found.", 404);
        var lines = await db.BillingInvoiceLines.AsNoTracking().Where(x => x.InvoiceId == invoice.Id)
            .OrderBy(x => x.CreatedAtUtc).Select(x => new InvoiceLineDto(
                x.Id, x.ServiceCode, x.Description, x.Quantity, x.UnitPrice, x.LineTotal)).ToListAsync(ct);
        var paid = await db.BillingPayments.Where(x => x.InvoiceId == invoice.Id).SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        return BillingResult<InvoiceDto>.Success(ToInvoiceDto(invoice, lines, paid));
    }

    public async Task<BillingResult<PaymentDto>> RecordPaymentAsync(
        Guid hospitalId, Guid invoiceId, PaymentCreateInput input, CancellationToken ct)
    {
        if (input.Amount <= 0 || decimal.Round(input.Amount, 2) != input.Amount)
            return BillingResult<PaymentDto>.Failure("Payment amount must be positive and have at most two decimal places.", 400);
        if (!PaymentMethods.IsSupported(input.Method))
            return BillingResult<PaymentDto>.Failure("Payment method must be Cash, UPI, Card or BankTransfer.", 400);
        if (input.Reference?.Trim().Length > 80)
            return BillingResult<PaymentDto>.Failure("Payment reference must be at most 80 characters.", 400);

        var invoice = await db.BillingInvoices.FirstOrDefaultAsync(
            x => x.Id == invoiceId && x.HospitalId == hospitalId, ct);
        if (invoice is null || !organizationScope.CanManageDepartment(hospitalId, invoice.DepartmentId))
            return BillingResult<PaymentDto>.Failure("Invoice not found.", 404);

        var paid = await db.BillingPayments.Where(x => x.InvoiceId == invoice.Id)
            .SumAsync(x => (decimal?)x.Amount, ct) ?? 0m;
        var due = invoice.Subtotal - paid;
        if (input.Amount > due)
            return BillingResult<PaymentDto>.Failure("Payment exceeds the outstanding invoice balance.", 409);
        if (due <= 0)
            return BillingResult<PaymentDto>.Failure("The invoice is already fully paid.", 409);

        // PaymentSequence and the EF concurrency token serialize concurrent payments
        // against this invoice. The payment and sequence update commit atomically
        // inside the request audit transaction.
        invoice.PaymentSequence = checked(invoice.PaymentSequence + 1);
        var payment = new BillingPayment
        {
            HospitalId = hospitalId, InvoiceId = invoice.Id,
            ReceiptNumber = string.Empty, Amount = input.Amount,
            Method = input.Method, Reference = string.IsNullOrWhiteSpace(input.Reference) ? null : input.Reference.Trim(),
            PaidAtUtc = DateTimeOffset.UtcNow
        };
        payment.ReceiptNumber = $"RCT-{payment.PaidAtUtc:yyyyMMdd}-{payment.Id.ToString("N")[..8].ToUpperInvariant()}";
        db.BillingPayments.Add(payment);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BillingResult<PaymentDto>.Failure("The invoice balance changed during payment. Refresh the invoice and retry.", 409);
        }
        return BillingResult<PaymentDto>.Success(ToDto(payment));
    }

    public async Task<BillingResult<ReceiptDto>> GetReceiptAsync(
        Guid hospitalId, Guid paymentId, CancellationToken ct)
    {
        var payment = await db.BillingPayments.AsNoTracking()
            .Join(db.BillingInvoices.AsNoTracking(), p => p.InvoiceId, i => i.Id, (p, i) => new { Payment = p, Invoice = i })
            .FirstOrDefaultAsync(x => x.Payment.Id == paymentId && x.Payment.HospitalId == hospitalId, ct);
        if (payment is null || !organizationScope.CanManageDepartment(hospitalId, payment.Invoice.DepartmentId))
            return BillingResult<ReceiptDto>.Failure("Receipt not found.", 404);
        return BillingResult<ReceiptDto>.Success(new(
            payment.Payment.ReceiptNumber, payment.Invoice.InvoiceNumber, hospitalId,
            payment.Invoice.PatientId, payment.Payment.Amount, payment.Payment.Method,
            payment.Payment.Reference, payment.Payment.PaidAtUtc));
    }

    public async Task<BillingResult<DailyCollectionReportDto>> GetDailyCollectionAsync(
        Guid hospitalId, DateOnly businessDate, string timeZoneId, CancellationToken ct)
    {
        if (!organizationScope.CanAccessHospital(hospitalId))
            return BillingResult<DailyCollectionReportDto>.Failure("Hospital not found.", 404);
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > 100)
            return BillingResult<DailyCollectionReportDto>.Failure("A valid time zone is required.", 400);
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId); }
        catch (TimeZoneNotFoundException) { return BillingResult<DailyCollectionReportDto>.Failure("The time zone is not recognized.", 400); }
        catch (InvalidTimeZoneException) { return BillingResult<DailyCollectionReportDto>.Failure("The time zone is invalid.", 400); }
        var startLocal = businessDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endLocal = businessDate.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, zone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, zone);
        var payments = await db.BillingPayments.AsNoTracking()
            .Where(x => x.HospitalId == hospitalId && x.PaidAtUtc >= startUtc && x.PaidAtUtc < endUtc)
            .GroupBy(x => x.Method)
            .Select(g => new { Method = g.Key, Total = g.Sum(x => x.Amount), Count = g.Count() })
            .ToListAsync(ct);
        var byMethod = payments.ToDictionary(x => x.Method, x => x.Total);
        return BillingResult<DailyCollectionReportDto>.Success(new(
            businessDate, timeZoneId, payments.Sum(x => x.Total), byMethod, payments.Sum(x => x.Count)));
    }

    private bool CanManageHospital(Guid hospitalId) => organizationScope.CanManageHospital(hospitalId);
    private static string? ValidateServiceItem(BillingServiceItemInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || input.Code.Trim().Length > 40)
            return "Service code is required and must be at most 40 characters.";
        if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Trim().Length > 160)
            return "Service name is required and must be at most 160 characters.";
        if (string.IsNullOrWhiteSpace(input.Category) || input.Category.Trim().Length > 80)
            return "Service category is required and must be at most 80 characters.";
        if (input.UnitPrice < 0 || decimal.Round(input.UnitPrice, 2) != input.UnitPrice)
            return "Service price must be non-negative and have at most two decimal places.";
        return null;
    }

    private static BillingServiceItemDto ToDto(BillingServiceItem x) =>
        new(x.Id, x.HospitalId, x.Code, x.Name, x.Category, x.UnitPrice, x.IsActive);
    private static InvoiceLineDto ToDto(BillingInvoiceLine x) =>
        new(x.Id, x.ServiceCode, x.Description, x.Quantity, x.UnitPrice, x.LineTotal);
    private static PaymentDto ToDto(BillingPayment x) =>
        new(x.Id, x.InvoiceId, x.ReceiptNumber, x.Amount, x.Method, x.Reference, x.PaidAtUtc);
    private static InvoiceDto ToInvoiceDto(BillingInvoice invoice, IReadOnlyList<InvoiceLineDto> lines, decimal paid) =>
        new(invoice.Id, invoice.HospitalId, invoice.QueueEntryId, invoice.PatientId, invoice.InvoiceNumber,
            invoice.IssuedAtUtc, invoice.Status, invoice.Subtotal, paid, invoice.Subtotal - paid, lines);
}
