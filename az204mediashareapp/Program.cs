using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Logging;
using Microsoft.FeatureManagement;
using Microsoft.Identity.Web;
using Microsoft.Identity.Web.UI;
using MVCMediaShareAppNew.Models.SettingsModels;
using MVCMediaShareAppNew.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add configuration
builder.Configuration
    .SetBasePath(builder.Environment.ContentRootPath)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true)
    .AddEnvironmentVariables();

if(!builder.Environment.IsDevelopment())
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
                        Path = options.CallbackPath
                    }.ToString();

                    context.ProtocolMessage.RedirectUri = redirectUri;
                    Console.WriteLine($"Redirect URI set to: {redirectUri}");
                    return Task.CompletedTask;
                }
            };
        });
}
else
{
    builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
        .AddMicrosoftIdentityWebApp(options => {
            builder.Configuration.GetSection("AzureAd").Bind(options);
            options.CallbackPath = "/signin-oidc";
        });
}

builder.Configuration.AddAzureAppConfiguration(options =>
{
    // Use Azure App Configuration
    var endpoint = builder.Configuration["AppConfiguration:Endpoint"];
    var connectionString = builder.Configuration["AppConfiguration:ConnectionString"];
    var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
    {
        ManagedIdentityClientId = builder.Configuration["ManagedIdentityClientId"]
    });
    options.Connect(new Uri(endpoint), credential)
    .ConfigureKeyVault(kv => kv.SetCredential(credential))
    .Select(KeyFilter.Any, LabelFilter.Null)
        .ConfigureRefresh(refresh =>
        {
            refresh.Register("AppConfig:Sentinel", refreshAll: true)
                .SetCacheExpiration(TimeSpan.FromSeconds(30));
        });
});

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

// Configure ServiceBus
builder.Services.Configure<ServiceBusSettings>(builder.Configuration.GetSection("ServiceBus"));

builder.Services.AddSingleton<ICosmosDbService, CosmosDbService>();
builder.Services.AddSingleton<IBlobStorageService, BlobStorageService>();
builder.Services.AddSingleton<IEventGridService, EventGridService>();

// Register queue services
builder.Services.AddSingleton<QueueService>();
builder.Services.AddSingleton<SBQueueService>();

// Register factory
builder.Services.AddSingleton<IQueueServiceFactory, QueueServiceFactory>();

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

// Add Feature Management
builder.Services.AddFeatureManagement();
builder.Services.AddAzureAppConfiguration();

// Configure logging
builder.Services.AddLogging(logging =>
{
    logging.AddConsole();
    logging.AddDebug();
    logging.SetMinimumLevel(LogLevel.Information);
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline
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
    app.MapControllers();
    app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}");
    app.MapRazorPages();
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Run();