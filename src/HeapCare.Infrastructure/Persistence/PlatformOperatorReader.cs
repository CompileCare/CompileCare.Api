using HeapCare.Application.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace HeapCare.Infrastructure.Persistence;

/// <summary>Resolves platform privileges from persisted operator records, never token role claims.</summary>
public sealed class PlatformOperatorReader(HeapCareDbContext db) : IPlatformOperatorReader
{
    public Task<bool> IsActiveAsync(string issuer, string subject, CancellationToken cancellationToken) =>
        db.PlatformOperators
            .AsNoTracking()
            .AnyAsync(
                value => value.Issuer == issuer && value.Subject == subject && value.IsActive,
                cancellationToken);
}
