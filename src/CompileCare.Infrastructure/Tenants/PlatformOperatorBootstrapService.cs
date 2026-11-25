using System.Data;
using System.Security.Cryptography;
using System.Text;
using CompileCare.Application.Tenancy;
using CompileCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CompileCare.Infrastructure.Tenants;

/// <summary>
/// One-time bootstrap for the first local platform operator. The API endpoint is
/// Development-only; the caller must also present a validated OIDC identity.
/// </summary>
public sealed class PlatformOperatorBootstrapService(CompileCareDbContext db) : IPlatformOperatorBootstrapService
{
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

        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        if (await db.PlatformOperators.AnyAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(false, true, false);
        }

        db.PlatformOperators.Add(new PlatformOperator
        {
            Issuer = issuer,
            Subject = subject,
            IsActive = true
        });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new(true, false, false);
        }
        catch (DbUpdateException exception) when
            (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(false, true, false);
        }
        catch (PostgresException exception) when
            (exception.SqlState == PostgresErrorCodes.SerializationFailure)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new(false, true, false);
        }
    }
}
