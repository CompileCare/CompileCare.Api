using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace CompileCare.Api.Security;

public static class DatabaseConnectionSecurity
{
    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
            return;

        var connectionString = configuration.GetConnectionString("CompileCare");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:CompileCare must be configured outside Development.");
        }

        NpgsqlConnectionStringBuilder connection;
        try
        {
            connection = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            // Do not include the original connection string in the exception;
            // it may contain credentials and will be surfaced in startup logs.
            throw new InvalidOperationException(
                "ConnectionStrings:CompileCare must be a valid PostgreSQL connection string.");
        }

        if (connection.SslMode != SslMode.VerifyFull)
        {
            throw new InvalidOperationException(
                "Production PostgreSQL connections must use SSL Mode=VerifyFull.");
        }
    }
}
