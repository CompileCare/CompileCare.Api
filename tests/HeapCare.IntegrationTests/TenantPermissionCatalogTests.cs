using HeapCare.Application.Inpatient;
using HeapCare.Application.Tenancy;
using Xunit;

namespace HeapCare.IntegrationTests;

public sealed class TenantPermissionCatalogTests
{
    [Fact]
    public void PermissionCatalog_ContainsUniqueStableCodesAndDescriptiveMetadata()
    {
        var definitions = TenantPermissionCatalog.Definitions;

        Assert.NotEmpty(definitions);
        Assert.Equal(
            definitions.Count,
            definitions.Select(permission => permission.Code).Distinct(StringComparer.Ordinal).Count());

        Assert.All(definitions, permission =>
        {
            Assert.False(string.IsNullOrWhiteSpace(permission.Code));
            Assert.False(string.IsNullOrWhiteSpace(permission.Module));
            Assert.False(string.IsNullOrWhiteSpace(permission.Resource));
            Assert.False(string.IsNullOrWhiteSpace(permission.Action));
            Assert.False(string.IsNullOrWhiteSpace(permission.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(permission.Description));
        });
    }

    [Fact]
    public void SystemRoleCatalog_PreservesExactlyTheFiveExistingTenantRolesAndTheirBaselineGrantCounts()
    {
        var expectedRoles = new[]
        {
            TenantRoles.HospitalAdmin,
            TenantRoles.FacilityManager,
            TenantRoles.FacilityViewer,
            TenantRoles.PatientRegistrar,
            TenantRoles.ClinicalUser
        };

        Assert.Equal(
            expectedRoles.OrderBy(role => role, StringComparer.Ordinal),
            TenantPermissionCatalog.SystemRoles
                .Select(role => role.Code)
                .OrderBy(role => role, StringComparer.Ordinal));

        Assert.Equal(26, TenantPermissionCatalog.GetDefaultPermissions(TenantRoles.HospitalAdmin).Count);
        Assert.Equal(2, TenantPermissionCatalog.GetDefaultPermissions(TenantRoles.FacilityManager).Count);
        Assert.Equal(1, TenantPermissionCatalog.GetDefaultPermissions(TenantRoles.FacilityViewer).Count);
        Assert.Equal(16, TenantPermissionCatalog.GetDefaultPermissions(TenantRoles.PatientRegistrar).Count);
        Assert.Equal(7, TenantPermissionCatalog.GetDefaultPermissions(TenantRoles.ClinicalUser).Count);

        Assert.Empty(TenantPermissionCatalog.GetDefaultPermissions("UnknownRole"));
        Assert.Empty(TenantPermissionCatalog.GetDefaultPermissions(null));
    }

    [Fact]
    public void EveryBaselineRoleGrant_ResolvesToAnAssignableCatalogEntry()
    {
        foreach (var role in TenantPermissionCatalog.SystemRoles)
        {
            Assert.NotEmpty(role.PermissionCodes);
            Assert.Equal(
                role.PermissionCodes.Count,
                role.PermissionCodes.Distinct(StringComparer.Ordinal).Count());

            foreach (var permissionCode in role.PermissionCodes)
            {
                Assert.NotNull(TenantPermissionCatalog.Find(permissionCode));
                Assert.True(TenantPermissionCatalog.IsAssignable(permissionCode));
            }
        }
    }

    [Fact]
    public void IpdPermissions_AreRegisteredForPolicyMetadataButCannotBeAssignedToRolesYet()
    {
        var ipdPermissions = new[]
        {
            InpatientPermissions.Read,
            InpatientPermissions.Admit,
            InpatientPermissions.BedAssign,
            InpatientPermissions.BedTransfer,
            InpatientPermissions.CancelAdmission,
            InpatientPermissions.Discharge
        };

        foreach (var permissionCode in ipdPermissions)
        {
            Assert.NotNull(TenantPermissionCatalog.Find(permissionCode));
            Assert.False(TenantPermissionCatalog.IsAssignable(permissionCode));
            Assert.DoesNotContain(
                TenantPermissionCatalog.SystemRoles.SelectMany(role => role.PermissionCodes),
                grantedPermission => grantedPermission == permissionCode);
        }
    }
}
