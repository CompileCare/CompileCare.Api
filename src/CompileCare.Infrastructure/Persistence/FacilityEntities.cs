namespace CompileCare.Infrastructure.Persistence;

public sealed class Hospital : TenantOwnedEntity
{
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public ICollection<Department> Departments { get; set; } = [];
}

public sealed class Department : TenantOwnedEntity
{
    public Guid HospitalId { get; set; }
    public Hospital Hospital { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public ICollection<Ward> Wards { get; set; } = [];
}

public sealed class Ward : TenantOwnedEntity
{
    public Guid DepartmentId { get; set; }
    public Department Department { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public ICollection<Room> Rooms { get; set; } = [];
}

public sealed class Room : TenantOwnedEntity
{
    public Guid WardId { get; set; }
    public Ward Ward { get; set; } = null!;
    public string Name { get; set; } = string.Empty;
    public ICollection<Bed> Beds { get; set; } = [];
}

public sealed class Bed : TenantOwnedEntity
{
    public Guid RoomId { get; set; }
    public Room Room { get; set; } = null!;
    public string Number { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}