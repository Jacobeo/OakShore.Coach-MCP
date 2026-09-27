using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.MsSql;
using Xunit;
using OakShore.Coach.Api;

namespace OakShore.Coach.Api.Tests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    private readonly MsSqlContainer database = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-CU20-ubuntu-22.04").Build();

    public string ConnectionString => database.GetConnectionString();
    public Task InitializeAsync() => database.StartAsync();
    public Task DisposeAsync() => database.DisposeAsync().AsTask();
}

public sealed class CoachHost(DatabaseFixture database) : WebApplicationFactory<Program>
{
    public bool DenyIngest { get; init; }
    public TimeProvider? Clock { get; init; }
    public const string PublicUrl = "https://coach.example";
    public const string Audience = PublicUrl + "/mcp";
    public const string Issuer = "https://issuer.example";
    private readonly RsaSecurityKey key = new(RSA.Create(2048)) { KeyId = "test-key" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Authentication:Audience", Audience)
            .UseSetting("Authentication:Authority", Issuer)
            .UseSetting("Server:PublicUrl", PublicUrl)
            .UseSetting("Persistence:ConnectionString", database.ConnectionString)
            .UseSetting("Ingest:BaseUrl", PublicUrl);
        builder.ConfigureServices(services =>
        {
            if (Clock is not null)
                services.AddSingleton(Clock);
            if (DenyIngest)
                services.AddAuthorization(options => options.AddPolicy("Athlete", policy =>
                    policy.RequireAuthenticatedUser().RequireAssertion(context =>
                        context.Resource is HttpContext http && http.Request.Path != "/ingest")));
            services.AddHttpClient("Ingest").ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                var metadata = new OpenIdConnectConfiguration { Issuer = Issuer };
                metadata.SigningKeys.Add(key);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(metadata);
            });
        });
    }

    public string Token(string? userId, string audience = Audience, DateTime? expires = null, string issuer = Issuer)
    {
        var token = new JwtSecurityToken(issuer, audience, userId is null ? [] : [new Claim("sub", userId)],
            DateTime.UtcNow.AddMinutes(-10), expires ?? DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
