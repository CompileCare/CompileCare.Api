using CompileCare.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace CompileCare.Infrastructure.Persistence;

/// <summary>Resolves platform privileges from persisted operator records, never token role claims.</summary>
public sealed class PlatformOperatorReader(CompileCareDbContext db) : IPlatformOperatorReader
{
    public Task<bool> IsActiveAsync(string issuer, string subject, CancellationToken cancellationToken) =>
        db.PlatformOperators
            .AsNoTracking()
            .AnyAsync(
                value => value.Issuer == issuer && value.Subject == subject && value.IsActive,
                cancellationToken);
}
