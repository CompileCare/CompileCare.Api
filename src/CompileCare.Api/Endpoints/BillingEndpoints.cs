using CompileCare.Api.Security;
using CompileCare.Application.Billing;

namespace CompileCare.Api;

public static class BillingEndpoints
{
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/hospitals/{hospitalId:guid}/billing")
            .WithTags("Billing")
            .WithMetadata(TenantScopedEndpointMetadata.Instance)
            .RequireAuthorization();

        group.MapPost("/service-items", async (Guid hospitalId, BillingServiceItemInput request, IBillingService service, CancellationToken ct) =>
        {
            var result = await service.CreateServiceItemAsync(hospitalId, request, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/hospitals/{hospitalId:D}/billing/service-items/{result.Value!.Id:D}", result.Value)
                : Results.Problem(title: "Billing service creation failed", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("CreateBillingServiceItem").RequireAuthorization(AuthorizationPolicies.BillingCatalogueManage);

        group.MapPut("/service-items/{serviceItemId:guid}", async (Guid hospitalId, Guid serviceItemId, BillingServiceItemInput request, IBillingService service, CancellationToken ct) =>
        {
            var result = await service.UpdateServiceItemAsync(hospitalId, serviceItemId, request, ct);
            return result.IsSuccess ? Results.Ok(result.Value) :
                Results.Problem(title: "Billing service update failed", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("UpdateBillingServiceItem").RequireAuthorization(AuthorizationPolicies.BillingCatalogueManage);

        group.MapGet("/service-items", async (Guid hospitalId, IBillingService service, CancellationToken ct, bool includeInactive = false) =>
        {
            var result = await service.ListServiceItemsAsync(hospitalId, includeInactive, ct);
            return result.IsSuccess ? Results.Ok(result.Value) :
                Results.Problem(title: "Billing service catalogue unavailable", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("ListBillingServiceItems").RequireAuthorization(AuthorizationPolicies.BillingInvoiceRead);

        group.MapPost("/invoices", async (Guid hospitalId, InvoiceCreateInput request, IBillingService service, CancellationToken ct) =>
        {
            var result = await service.CreateInvoiceAsync(hospitalId, request, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/hospitals/{hospitalId:D}/billing/invoices/{result.Value!.Id:D}", result.Value)
                : Results.Problem(title: "Invoice creation failed", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("CreateBillingInvoice").RequireAuthorization(AuthorizationPolicies.BillingInvoiceManage);

        group.MapGet("/visits/{queueEntryId:guid}/invoice", async (Guid hospitalId, Guid queueEntryId, IBillingService service, CancellationToken ct) =>
        {
            var result = await service.GetInvoiceForVisitAsync(hospitalId, queueEntryId, ct);
            return result.IsSuccess ? Results.Ok(result.Value) :
                Results.Problem(title: "Visit invoice not found", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("GetBillingInvoiceForVisit").RequireAuthorization(AuthorizationPolicies.BillingInvoiceRead);

        group.MapGet("/invoices/{invoiceId:guid}", async (Guid hospitalId, Guid invoiceId, IBillingService service, CancellationToken ct) =>
        {
            var result = await service.GetInvoiceAsync(hospitalId, invoiceId, ct);
            return result.IsSuccess ? Results.Ok(result.Value) :
                Results.Problem(title: "Invoice not found", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("GetBillingInvoice").RequireAuthorization(AuthorizationPolicies.BillingInvoiceRead);

        group.MapGet("/invoices/{invoiceId:guid}/payments", async (
            Guid hospitalId, Guid invoiceId, IBillingService service, CancellationToken ct) =>
        {
            var result = await service.ListPaymentsAsync(hospitalId, invoiceId, ct);
            return result.IsSuccess ? Results.Ok(result.Value) :
                Results.Problem(title: "Invoice payment history not found", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("ListBillingInvoicePayments").RequireAuthorization(AuthorizationPolicies.BillingInvoiceRead);

        group.MapPost("/invoices/{invoiceId:guid}/payments", async (Guid hospitalId, Guid invoiceId, PaymentCreateInput request, IBillingService service, CancellationToken ct) =>
        {
            var result = await service.RecordPaymentAsync(hospitalId, invoiceId, request, ct);
            return result.IsSuccess
                ? Results.Created($"/api/v1/hospitals/{hospitalId:D}/billing/payments/{result.Value!.Id:D}/receipt", result.Value)
                : Results.Problem(title: "Payment recording failed", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("RecordBillingPayment").RequireAuthorization(AuthorizationPolicies.BillingPaymentManage);

        group.MapGet("/payments/{paymentId:guid}/receipt", async (Guid hospitalId, Guid paymentId, IBillingService service, CancellationToken ct) =>
        {
            var result = await service.GetReceiptAsync(hospitalId, paymentId, ct);
            return result.IsSuccess ? Results.Ok(result.Value) :
                Results.Problem(title: "Receipt not found", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("GetBillingReceipt").RequireAuthorization(AuthorizationPolicies.BillingInvoiceRead);

        group.MapGet("/reports/daily-collection", async (Guid hospitalId, DateOnly businessDate, string timeZoneId, IBillingService service, CancellationToken ct) =>
        {
            var result = await service.GetDailyCollectionAsync(hospitalId, businessDate, timeZoneId, ct);
            return result.IsSuccess ? Results.Ok(result.Value) :
                Results.Problem(title: "Daily collection report unavailable", detail: result.Error, statusCode: result.StatusCode);
        }).WithName("GetDailyBillingCollection").RequireAuthorization(AuthorizationPolicies.BillingReportRead);

        return endpoints;
    }
}
