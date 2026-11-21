using Microsoft.Extensions.Configuration;

namespace GreenCarWash.BuildingBlocks.Data;

public static class SqlConnectionStrings
{
    public static string ForDatabase(IConfiguration configuration, string connectionName, string database)
    {
        var server = Environment.GetEnvironmentVariable("GCW_SQL_SERVER");
        var user = Environment.GetEnvironmentVariable("GCW_SQL_USER");
        if (!string.IsNullOrWhiteSpace(server) || !string.IsNullOrWhiteSpace(user))
        {
            if (string.IsNullOrWhiteSpace(server))
                server = "localhost";

            if (!string.IsNullOrWhiteSpace(user))
            {
                var password = Environment.GetEnvironmentVariable("GCW_SQL_PASSWORD") ?? string.Empty;
                return $"Server={server};Database={database};User Id={user};Password={password};TrustServerCertificate=True";
            }

            return $"Server={server};Database={database};Trusted_Connection=True;TrustServerCertificate=True";
        }

        return configuration.GetConnectionString(connectionName)
            ?? $"Server=localhost;Database={database};Trusted_Connection=True;TrustServerCertificate=True";
    }
}
