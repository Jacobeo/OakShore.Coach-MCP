using OakShore.Coach.Domain;
using OakShore.Coach.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;

namespace OakShore.Coach.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var audience = builder.Configuration["Authentication:Audience"]
            ?? throw new InvalidOperationException("Authentication:Audience must be the public server URL.");
        var authority = builder.Configuration["Authentication:Authority"]
            ?? throw new InvalidOperationException("Authentication:Authority must name the hosted issuer.");
        var metadataUrl = $"{audience.TrimEnd('/')}/.well-known/oauth-protected-resource";

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = authority;
            options.Audience = audience;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                ClockSkew = TimeSpan.FromSeconds(30)
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    var subjects = context.Principal!.FindAll("sub").ToArray();
                    if (subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0].Value)
                        || subjects[0].Value.Length > 255 || subjects[0].Value != subjects[0].Value.Trim())
                        context.Fail("A single, nonblank subject of at most 255 characters without surrounding whitespace is required.");
                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    context.HandleResponse();
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    context.Response.Headers.WWWAuthenticate = $"Bearer resource_metadata=\"{metadataUrl}\"";
                    return Task.CompletedTask;
                }
            };
        });
        builder.Services.AddAuthorization(options => options.AddPolicy("Athlete", policy =>
            policy.RequireAuthenticatedUser().RequireClaim("sub")));
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()
            .WithExposedHeaders("WWW-Authenticate", "MCP-Protocol-Version")));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<AthleteProfileStore>();
        builder.Services.AddScoped<IAthleteProfileRepository, AthleteProfileRepository>();
        builder.Services.AddScoped<AthleteProfileService>();
        builder.Services.AddScoped<AthleteProfileIngestService>();
        builder.Services.AddHttpClient("Ingest", (services, client) =>
        {
            client.BaseAddress = new Uri(builder.Configuration["Ingest:BaseUrl"] ?? audience);
            var request = services.GetRequiredService<IHttpContextAccessor>().HttpContext!.Request;
            client.DefaultRequestHeaders.Authorization = System.Net.Http.Headers.AuthenticationHeaderValue.Parse(request.Headers.Authorization.ToString());
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddTypedClient<IAthleteProfileIngestGateway, AthleteProfileIngestGateway>();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow);
        builder.Services.AddMcpServer().WithHttpTransport(options =>
            options.SessionMode = HttpServerSessionMode.Stateless).WithTools<AthleteProfileTools>();

        var app = builder.Build();
        await using (var scope = app.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<AthleteProfileStore>().InitializeAsync(CancellationToken.None);
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/.well-known/oauth-protected-resource", () => Results.Ok(new
        {
            resource = audience,
            authorization_servers = new[] { authority },
            bearer_methods_supported = new[] { "header" }
        }));
        app.MapMcp("/mcp").RequireAuthorization("Athlete");
        app.MapPost("/ingest", async (AthleteProfileBatch batch, HttpContext context, AthleteProfileIngestService ingest, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await ingest.IngestAsync(context.User.FindFirst("sub")!.Value, batch, cancellationToken));
            }
            catch (ArgumentException error)
            {
                return Results.BadRequest(new { error = error.Message, lastSyncedAt = (DateTimeOffset?)null });
            }
        }).RequireAuthorization("Athlete");
        await app.RunAsync();
    }
}
