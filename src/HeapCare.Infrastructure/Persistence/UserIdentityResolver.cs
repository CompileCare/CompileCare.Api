using Microsoft.EntityFrameworkCore;

namespace HeapCare.Infrastructure.Persistence;

/// <summary>
/// Resolves a validated external principal to one global identity row. The
/// insert is an idempotent PostgreSQL upsert so concurrent tenant/platform
/// provisioning cannot create duplicate identity records.
/// </summary>
public static class UserIdentityResolver
{
    public static async Task<UserIdentity> ResolveAsync(
        HeapCareDbContext db,
        string issuer,
        string subject,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (string.IsNullOrWhiteSpace(issuer) || issuer.Length > 255)
            throw new ArgumentException("A validated OIDC issuer is required and must not exceed 255 characters.", nameof(issuer));
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 255)
            throw new ArgumentException("A validated OIDC subject is required and must not exceed 255 characters.", nameof(subject));

        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "user_identities"
                ("Id", "Issuer", "Subject", "CreatedAtUtc", "UpdatedAtUtc", "Version")
            VALUES
                ({id}, {issuer}, {subject}, {now}, {now}, {Guid.NewGuid()})
            ON CONFLICT ("Issuer", "Subject") DO NOTHING
            """,
            cancellationToken);

        return await db.UserIdentities.SingleAsync(
            value => value.Issuer == issuer && value.Subject == subject,
            cancellationToken);
    }
}
