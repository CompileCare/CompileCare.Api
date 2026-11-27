using HeapCare.Application.Appointments;
using HeapCare.Application.Auditing;
using HeapCare.Application.Billing;
using HeapCare.Application.Clinical;
using HeapCare.Application.Patients;
using HeapCare.Application.Staff;
using HeapCare.Application.Tenancy;

namespace HeapCare.Api.Security;

internal static class TenantRolePermissions
{
    public static IReadOnlyList<string> GetPermissions(string role) =>
        role switch
        {
            TenantRoles.HospitalAdmin =>
            [
                TenantPermissions.FacilityRead,
                TenantPermissions.FacilityWrite,
                TenantPermissions.FacilityDelete,
                TenantPermissions.MembershipManage,
                AuditPermissions.Read,
                PatientPermissions.Read,
                PatientPermissions.IdentityRead,
                PatientPermissions.Register,
                PatientDuplicatePermissions.Review,
                PatientIdentifierPermissions.Read,
                PatientIdentifierPermissions.Assign,
                PatientIdentifierPermissions.PolicyRead,
                PatientIdentifierPermissions.PolicyManage,
                StaffPermissions.Read,
                StaffPermissions.Manage,
                AppointmentPermissions.AvailabilityRead,
                AppointmentPermissions.AvailabilityManage,
                AppointmentPermissions.AppointmentRead,
                AppointmentPermissions.AppointmentManage,
                QueuePermissions.Read,
                QueuePermissions.Manage,
                BillingPermissions.CatalogueManage,
                BillingPermissions.InvoiceRead,
                BillingPermissions.InvoiceManage,
                BillingPermissions.PaymentManage,
                BillingPermissions.ReportRead
            ],
            TenantRoles.FacilityManager =>
            [
                TenantPermissions.FacilityRead,
                TenantPermissions.FacilityWrite
            ],
            TenantRoles.FacilityViewer =>
            [
                TenantPermissions.FacilityRead
            ],
            TenantRoles.PatientRegistrar =>
            [
                TenantPermissions.FacilityRead,
                PatientPermissions.Read,
                PatientPermissions.IdentityRead,
                PatientPermissions.Register,
                PatientDuplicatePermissions.Review,
                PatientIdentifierPermissions.Read,
                PatientIdentifierPermissions.Assign,
                AppointmentPermissions.AvailabilityRead,
                AppointmentPermissions.AppointmentRead,
                AppointmentPermissions.AppointmentManage,
                QueuePermissions.Read,
                QueuePermissions.Manage,
                BillingPermissions.InvoiceRead,
                BillingPermissions.InvoiceManage,
                BillingPermissions.PaymentManage,
                BillingPermissions.ReportRead
            ],
            TenantRoles.ClinicalUser =>
            [
                PatientPermissions.IdentityRead,
                TenantPermissions.FacilityRead,
                QueuePermissions.Read,
                OpdEncounterPermissions.Read,
                OpdEncounterPermissions.Manage,
                OpdEncounterPermissions.Sign,
                OpdEncounterPermissions.Amend
            ],
            _ => Array.Empty<string>()
        };
}
