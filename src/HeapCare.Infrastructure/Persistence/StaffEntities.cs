namespace HeapCare.Infrastructure.Persistence;

public sealed class StaffProfile : TenantOwnedEntity
{
    public string StaffCode { get; set; } = string.Empty;
    public string GivenName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string? FamilyName { get; set; }
    public string? ProviderDesignation { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class StaffAssignment : TenantOwnedEntity
{
    public Guid StaffProfileId { get; set; }
    public Guid HospitalId { get; set; }
    public Guid? DepartmentId { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    public bool IsActive { get; set; } = true;
}
