using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;

namespace GameOfLife.Api.Tests;

/// <summary>Hosts the API in memory, in the Production environment.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    // Nothing connects to the database yet; this only has to pass the startup check.
    private const string UnusedConnectionString = "Host=localhost;Database=unused";

    /// <summary>A copy of this host with some configuration values replaced.</summary>
    public WebApplicationFactory<Program> WithSettings(params (string Key, string Value)[] settings) =>
        WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    // UseSetting rather than ConfigureAppConfiguration: Program reads the connection string before
    // builder.Build(), and only UseSetting values are visible to it by then.
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Production);
        builder.UseSetting("ConnectionStrings:GameOfLife", UnusedConnectionString);
    }
}