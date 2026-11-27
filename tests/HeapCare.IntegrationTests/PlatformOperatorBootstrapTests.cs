using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HeapCare.Application.Tenancy;
using HeapCare.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HeapCare.IntegrationTests;

[Collection("PostgreSQL integration")]
public sealed class PlatformOperatorBootstrapTests
{
    private readonly FacilityApiFactory _factory;

    public PlatformOperatorBootstrapTests(FacilityApiFactory factory)
    {
        _factory = factory;
        _factory.ResetDatabase();
    }

    [Fact]
    public async Task BootstrapEndpoint_IsNotMappedOutsideDevelopment()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", TestJwtTokenFactory.CreateToken());

        var response = await client.PostAsJsonAsync(
            "/api/v1/platform/bootstrap/operator",
            new { bootstrapSecret = "test-secret" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_RequiresSecretAndCreatesOnlyOnePersistedOperator()
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IPlatformOperatorBootstrapService>();

        var invalid = await service.BootstrapAsync(
            TestJwtTokenFactory.Issuer, TestJwtTokenFactory.Subject,
            "expected-secret", "wrong-secret", CancellationToken.None);
        Assert.True(invalid.InvalidSecret);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<HeapCareDbContext>()
            .PlatformOperators.ToListAsync());

        var created = await service.BootstrapAsync(
            TestJwtTokenFactory.Issuer, TestJwtTokenFactory.Subject,
            "expected-secret", "expected-secret", CancellationToken.None);
        Assert.True(created.Created);
        Assert.False(created.AlreadyInitialized);

        var secondIdentity = await service.BootstrapAsync(
            TestJwtTokenFactory.Issuer, "another-subject",
            "expected-secret", "expected-secret", CancellationToken.None);
        Assert.True(secondIdentity.AlreadyInitialized);
        Assert.False(secondIdentity.Created);

        var operators = await scope.ServiceProvider.GetRequiredService<HeapCareDbContext>()
            .PlatformOperators.ToListAsync();
        var platformOperator = Assert.Single(operators);
        Assert.Equal(TestJwtTokenFactory.Issuer, platformOperator.Issuer);
        Assert.Equal(TestJwtTokenFactory.Subject, platformOperator.Subject);
        Assert.True(platformOperator.IsActive);
    }

    [Fact]
    public async Task Bootstrap_ParticipatesInAmbientTransaction_AndRollsBackWithOwner()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HeapCareDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IPlatformOperatorBootstrapService>();

        await using var transaction = await db.Database.BeginTransactionAsync();

        var result = await service.BootstrapAsync(
            TestJwtTokenFactory.Issuer,
            TestJwtTokenFactory.Subject,
            "expected-secret",
            "expected-secret",
            CancellationToken.None);

        Assert.True(result.Created);
        Assert.NotNull(db.Database.CurrentTransaction);

        await transaction.RollbackAsync();
        db.ChangeTracker.Clear();

        Assert.Empty(await db.PlatformOperators.ToListAsync());
    }
}
