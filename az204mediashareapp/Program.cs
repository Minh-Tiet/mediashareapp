using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Azure.Cosmos.Serialization.HybridRow.Schemas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using MVCMediaShareAppNew.Models;
using MVCMediaShareAppNew.Services;
using StackExchange.Redis;
using System.Security.Claims;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);

// Add development-specific configuration
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

if (!builder.Environment.IsDevelopment())
{
    // Configure authentication with Microsoft Identity Web
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(options =>
        {
            builder.Configuration.GetSection("AzureAd").Bind(options);
            options.CallbackPath = "/signin-oidc";
            // Force HTTPS for redirect_uri
            options.Events = new OpenIdConnectEvents
            {
                OnRedirectToIdentityProvider = context =>
                {
                    var request = context.HttpContext.Request;
                    var redirectUri = new UriBuilder
                    {
                        Scheme = "https",
                        Host = request.Host.Host,
                        //Port = 443, // Force HTTPS port
                        Path = options.CallbackPath
                    }.ToString();

                    context.ProtocolMessage.RedirectUri = redirectUri;
                    Console.WriteLine($"Redirect URI set to: {redirectUri}"); // Debug log
                    return Task.CompletedTask;
                }
            };
        });
} 
else
{

    // In Development, configure a fake authentication scheme
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = "FakeAuth";
        options.DefaultChallengeScheme = "FakeAuth";
    }).AddScheme<AuthenticationSchemeOptions, FakeAuthHandler>("FakeAuth", options => { });
}

builder.Services.AddControllersWithViews(options =>
{
    if (!builder.Environment.IsDevelopment())
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        options.Filters.Add(new AuthorizeFilter(policy));
    }
});

builder.Services.AddRazorPages()
    .AddMicrosoftIdentityUI();

// Configure Cosmos DB
builder.Services.Configure<CosmosDbSettings>(builder.Configuration.GetSection("CosmosDb"));

// Configure Azure Storage
builder.Services.Configure<AzureStorageSettings>(builder.Configuration.GetSection("AzureStorage"));

builder.Services.AddSingleton<ICosmosDbService, CosmosDbService>();
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddSingleton<IQueueService, QueueService>();

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
{
    if (!builder.Environment.IsDevelopment())
    {


        var keyVaultUri = builder.Configuration["KeyVault:VaultUri"] ?? "";
        var credentials = new DefaultAzureCredential();
        var secretClient = new SecretClient(new Uri(keyVaultUri), credentials);
        var redisSecretResponse = secretClient.GetSecret("Redis-ConnectionString");
        var redisConnectionString = redisSecretResponse.Value.Value;
        return ConnectionMultiplexer.Connect(redisConnectionString);
    }
    else
    {
        var redisConnectionString = builder.Configuration["Redis:ConnectionString"];
        return ConnectionMultiplexer.Connect(redisConnectionString ?? "localhost:6379");
    }
});
// Configure logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
    logging.SetMinimumLevel(LogLevel.Information);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();

    // Handle forwarded headers from Azure App Service
    app.UseForwardedHeaders(new ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
    });
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseEndpoints(app =>
{
    app.MapControllers(); // Added to map API controllers
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
    app.MapRazorPages();
});

app.Run();


// Fake authentication handler for development
public class FakeAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public FakeAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, ISystemClock clock)
        : base(options, logger, encoder, clock) { }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Simulate an authenticated user
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, "TestUser"),
            new Claim(ClaimTypes.NameIdentifier, "test-user-id"),
            new Claim(ClaimTypes.Role, "User") // Add roles if needed
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}