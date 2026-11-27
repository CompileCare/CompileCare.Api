using Xunit;

namespace HeapCare.IntegrationTests;

[CollectionDefinition("PostgreSQL integration", DisableParallelization = true)]
public sealed class PostgreSqlIntegrationCollection : ICollectionFixture<FacilityApiFactory>
{
}
