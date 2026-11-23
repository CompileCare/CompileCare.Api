using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompileCare.Infrastructure.Persistence.Configurations;

public sealed class HospitalConfiguration : IEntityTypeConfiguration<Hospital>
{
    public void Configure(EntityTypeBuilder<Hospital> b)
    {
        b.ToTable("hospitals");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Code).HasMaxLength(50);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("\"Code\" IS NOT NULL");
        b.HasMany(x => x.Departments).WithOne(x => x.Hospital)
            .HasForeignKey(x => new { x.TenantId, x.HospitalId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class DepartmentConfiguration : IEntityTypeConfiguration<Department>
{
    public void Configure(EntityTypeBuilder<Department> b)
    {
        b.ToTable("departments");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Code).HasMaxLength(50);
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.HospitalId, x.Name }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.HospitalId, x.Code }).IsUnique().HasFilter("\"Code\" IS NOT NULL");
        b.HasMany(x => x.Wards).WithOne(x => x.Department)
            .HasForeignKey(x => new { x.TenantId, x.DepartmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class WardConfiguration : IEntityTypeConfiguration<Ward>
{
    public void Configure(EntityTypeBuilder<Ward> b)
    {
        b.ToTable("wards");
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.DepartmentId, x.Name }).IsUnique();
        b.HasMany(x => x.Rooms).WithOne(x => x.Ward)
            .HasForeignKey(x => new { x.TenantId, x.WardId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> b)
    {
        b.ToTable("rooms");
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.WardId, x.Name }).IsUnique();
        b.HasMany(x => x.Beds).WithOne(x => x.Room)
            .HasForeignKey(x => new { x.TenantId, x.RoomId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
public sealed class BedConfiguration : IEntityTypeConfiguration<Bed>
{
    public void Configure(EntityTypeBuilder<Bed> b)
    {
        b.ToTable("beds");
        b.Property(x => x.Number).HasMaxLength(50).IsRequired();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.RoomId, x.Number }).IsUnique();
    }
}
