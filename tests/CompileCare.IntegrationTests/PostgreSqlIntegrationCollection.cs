using Xunit;

namespace CompileCare.IntegrationTests;

[CollectionDefinition("PostgreSQL integration", DisableParallelization = true)]
public sealed class PostgreSqlIntegrationCollection : ICollectionFixture<FacilityApiFactory>;
