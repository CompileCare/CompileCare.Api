using CompileCare.Application.Tenancy;
namespace CompileCare.UnitTests.Tenancy;
public sealed class TenantContextTests
{
    [Fact] public void SetTenant_StoresId() { var sut = new TenantContext(); var id = Guid.NewGuid(); sut.SetTenant(id); Assert.Equal(id, sut.TenantId); }
    [Fact] public void SetTenant_RejectsEmptyId() { var sut = new TenantContext(); Assert.Throws<ArgumentException>(() => sut.SetTenant(Guid.Empty)); }
    [Fact] public void SetTenant_CannotSwitchTenant() { var sut = new TenantContext(); sut.SetTenant(Guid.NewGuid()); Assert.Throws<InvalidOperationException>(() => sut.SetTenant(Guid.NewGuid())); }
}
