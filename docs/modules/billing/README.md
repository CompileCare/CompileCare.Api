# Billing API

**Status:** Initial OPD billing baseline — implementation in progress.

All routes are tenant-scoped and require an active tenant membership plus hospital/department scope. Billing permissions are distinct from clinical encounter permissions.

Base path: `/api/v1/hospitals/{hospitalId}/billing`

| Method | Route | Permission | Purpose |
|---|---|---|---|
| POST | `/service-items` | `billing.catalogue.manage` | Create a hospital service/tariff |
| PUT | `/service-items/{serviceItemId}` | `billing.catalogue.manage` | Update tariff metadata/price or deactivate |
| GET | `/service-items?includeInactive=false` | `billing.invoice.read` | List catalogue |
| POST | `/invoices` | `billing.invoice.manage` | Issue invoice for an eligible OPD queue visit |
| GET | `/invoices/{invoiceId}` | `billing.invoice.read` | Read invoice, line snapshots and current balance |
| POST | `/invoices/{invoiceId}/payments` | `billing.payment.manage` | Record cash/UPI/card/bank-transfer payment |
| GET | `/payments/{paymentId}/receipt` | `billing.invoice.read` | Retrieve receipt view |
| GET | `/reports/daily-collection?businessDate=YYYY-MM-DD&timeZoneId=...` | `billing.report.read` | Hospital-local daily collection totals |

## Request contracts

### Service item

```json
{
  "code": "CONSULT",
  "name": "OPD Consultation",
  "category": "Consultation",
  "unitPrice": 500.00,
  "isActive": true
}
```

### Invoice

```json
{
  "queueEntryId": "00000000-0000-0000-0000-000000000000",
  "lines": [
    { "serviceItemId": "00000000-0000-0000-0000-000000000000", "quantity": 1 }
  ]
}
```

Invoice creation is accepted only for an in-progress or completed queue visit. The visit must belong to the selected hospital and the caller must have department scope. One initial invoice per queue visit is enforced. Service code, description and unit price are snapshotted onto invoice lines.

### Payment

```json
{
  "amount": 200.00,
  "method": "Cash",
  "reference": null
}
```

Supported methods: `Cash`, `UPI`, `Card`, `BankTransfer`. The optional reference is a short non-sensitive reconciliation reference; never send payment credentials, card PAN/CVV or UPI PIN.

## Role defaults

- HospitalAdmin: catalogue management, invoice read/manage, payment manage and daily report.
- PatientRegistrar: invoice read/manage, payment manage and daily report.
- ClinicalUser: no billing permissions.
- FacilityManager / FacilityViewer: no billing permissions.

These are initial application role mappings, not a substitute for pilot role review. A hospital should provision billing work only to staff with an approved finance/reception responsibility.

## Consistency and data protection

- Tenant query filters and composite foreign keys bind service, invoice, visit, patient registration and payment to the same tenant/hospital.
- Invoice and line snapshots are immutable after issue. Service catalogue price changes affect future invoices only.
- Payments are append-only. The invoice payment sequence is a concurrency token; the payment and sequence update are committed with the request audit transaction. Stale concurrent payment attempts return conflict.
- Amounts use decimal precision 18,2 and are INR in this baseline.
- Daily report requires hospital-wide scope because it aggregates all departments; department-only memberships cannot read hospital totals.
- Billing audit events contain route/action/resource metadata only, never invoice line clinical context or payment credentials.

## Explicitly not implemented

GST/tax calculation and statutory invoice format, legal invoice series, discounts/waivers, invoice cancellation/credit notes, refunds/reversals, deposits/advances, insurance/TPA, payment gateway, accounting integration, cashier close/shift reconciliation, ageing and advanced finance reports. See the product [OPD billing foundation and deferred scope](https://github.com/CompileCare/Compilecare.Doc/blob/main/product/workflows/opd-billing-foundation.md).

The initial internal invoice/receipt references are operational identifiers only. Do not represent them as compliant tax invoices until finance/legal validation is complete.
