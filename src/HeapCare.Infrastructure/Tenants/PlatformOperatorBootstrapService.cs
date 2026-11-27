using System.Data;
using System.Security.Cryptography;
using System.Text;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace HeapCare.Infrastructure.Tenants;

/// <summary>
/// One-time bootstrap for the first local platform operator. The API endpoint is
/// Development-only; the caller must also present a validated OIDC identity.
/// </summary>
public sealed class PlatformOperatorBootstrapService(HeapCareDbContext db) : IPlatformOperatorBootstrapService
{
    // Stable, application-specific PostgreSQL advisory-lock key. This is a
    // transaction-scoped lock, so PostgreSQL releases it on commit or rollback.
    private const long BootstrapAdvisoryLockKey = 0x43434F5045524154;

    public async Task<PlatformOperatorBootstrapResult> BootstrapAsync(
        string issuer,
        string subject,
        string configuredSecret,
        string suppliedSecret,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuredSecret) ||
            string.IsNullOrWhiteSpace(suppliedSecret) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(configuredSecret),
                Encoding.UTF8.GetBytes(suppliedSecret)))
            return new(false, false, true);

        if (string.IsNullOrWhiteSpace(issuer) || issuer.Length > 255 ||
            string.IsNullOrWhiteSpace(subject) || subject.Length > 255)
            return new(false, false, false);

        // AuditEventMiddleware owns the request transaction for this endpoint.
        // Direct service callers (for example, integration tests or other
        // application entry points) still get a transaction owned by this service.
        IDbContextTransaction? ownedTransaction = null;

        try
        {
            if (db.Database.CurrentTransaction is null)
            {
                ownedTransaction = await db.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
            }

            // Serialize bootstrap attempts even when participating in the
            // middleware's existing transaction, whose isolation level is not
            // controlled by this service. The lock is released with that transaction.
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT pg_advisory_xact_lock({BootstrapAdvisoryLockKey})",
                cancellationToken);

            if (await db.PlatformOperators.AnyAsync(cancellationToken))
            {
                if (ownedTransaction is not null)
                    await ownedTransaction.CommitAsync(cancellationToken);

                return new(false, true, false);
            }

            db.PlatformOperators.Add(new PlatformOperator
            {
                Issuer = issuer,
                Subject = subject,
                IsActive = true
            });

            await db.SaveChangesAsync(cancellationToken);

            if (ownedTransaction is not null)
                await ownedTransaction.CommitAsync(cancellationToken);

            return new(true, false, false);
        }
        catch
        {
            if (ownedTransaction is not null)
                await ownedTransaction.RollbackAsync(CancellationToken.None);

            throw;
        }
        finally
        {
            if (ownedTransaction is not null)
                await ownedTransaction.DisposeAsync();
        }
    }
}
