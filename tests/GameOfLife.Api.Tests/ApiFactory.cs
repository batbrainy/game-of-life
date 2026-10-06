using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace GameOfLife.Api.Tests;

/// <summary>Hosts the API in memory, in the Production environment, on a migrated database of its own.</summary>
public sealed class ApiFactory(PostgresFixture postgres) : WebApplicationFactory<Program>, IAsyncLifetime
{
    private string _connectionString = "";

    /// <summary>A copy of this host with some configuration values replaced.</summary>
    public WebApplicationFactory<Program> WithSettings(params (string Key, string Value)[] settings) =>
        WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    // xUnit runs this before the first test that uses this factory, and so before the host is built.
    public async ValueTask InitializeAsync() => _connectionString = await postgres.CreateMigratedDatabaseAsync();

    // UseSetting rather than ConfigureAppConfiguration: Program reads the connection string before
    // builder.Build(), and only UseSetting values are visible to it by then.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:GameOfLife", _connectionString);
    }
}