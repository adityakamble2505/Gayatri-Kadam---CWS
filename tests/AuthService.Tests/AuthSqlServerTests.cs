using FluentAssertions;
using Microsoft.Data.SqlClient;
using NUnit.Framework;

namespace GreenCarWash.AuthService.Tests;

public sealed class AuthSqlServerTests
{
    [Test]
    public void Database_Opens_WhenConnectionStringIsConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("GCW_TEST_SQL");
        if (string.IsNullOrWhiteSpace(connectionString))
            Assert.Ignore("GCW_TEST_SQL is not set.");

        using var connection = new SqlConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT DB_NAME()";
        var database = command.ExecuteScalar() as string;
        database.Should().NotBeNullOrWhiteSpace();
    }
}
