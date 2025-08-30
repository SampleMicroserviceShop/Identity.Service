using System.Security.Cryptography.X509Certificates;
using Common.Library.Configuration;
using Common.Library.MassTransit;
using Identity.Service.Entities;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Bson.Serialization;
using MongoDB.Bson;
using Common.Library.Settings;
using Identity.Service.Exceptions;
using Identity.Service.Settings;
using Identity.Service.HostedServices;
using Common.Library.HealthChecks;
using Common.Library.Logging;
using Common.Library.OpenTelemetry;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection;
using MassTransit;
using Microsoft.AspNetCore.Http.Extensions;
using System.Collections;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.ConfigureAzureKeyVault(builder.Environment);

// Configure Data Protection to persist keys
ConfigureDataProtection(builder);


const string AllowedOriginSetting = "AllowedOrigin";
//var connectionString = builder.Configuration.GetConnectionString("IdentityDataContextConnection") ?? throw new InvalidOperationException("Connection string 'IdentityDataContextConnection' not found.");;

//builder.Services.AddDbContext<IdentityDataContext>(options => options.UseSqlServer(connectionString));

//builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true).AddEntityFrameworkStores<IdentityDataContext>();

// Add services to the container.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var serviceSettings = builder.Configuration.GetSection(nameof(ServiceSettings)).Get<ServiceSettings>();
var mongoDbSettings = builder.Configuration.GetSection(nameof(MongoDbSettings)).Get<MongoDbSettings>();

builder.Services
    .Configure<IdentitySettings>(builder.Configuration.GetSection(nameof(IdentitySettings)))
    .AddDefaultIdentity<ApplicationUser>()
    .AddRoles<ApplicationRole>()
    .AddMongoDbStores<ApplicationUser, ApplicationRole, Guid>
    (
        mongoDbSettings.ConnectionString,
        serviceSettings.ServiceName
    );


builder.Services.AddMassTransitWithMessageBroker(builder.Configuration, retryConfigurator =>
{
    retryConfigurator.Interval(3, TimeSpan.FromSeconds(5));
    retryConfigurator.Ignore(typeof(UnknownUserException));
    retryConfigurator.Ignore(typeof(InsufficientFundsException));
});

builder.Services.AddSeqLogging(builder.Configuration)
    .AddTracing(builder.Configuration);


AddIdentityServer(builder);

builder.Services.AddLocalApiAuthentication();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});
builder.Services.AddControllers();
builder.Services.AddHostedService<IdentitySeedHostedService>();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks()
    .AddMongoDbHealthCheck();

// Configure Kestrel timeout settings for Azure environment
builder.WebHost.ConfigureKestrel(options =>
{
    // Azure Load Balancer has a 4-minute timeout, so we set KeepAlive to 3 minutes
    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(3);
    
    // Request headers timeout - reasonable for most requests
    options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(30);
    
    // Additional Azure-optimized settings
    options.Limits.MaxRequestBodySize = 30 * 1024 * 1024; // 30MB
    options.Limits.MaxConcurrentConnections = 100;
    options.Limits.MaxConcurrentUpgradedConnections = 100;
});


builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto |
                               ForwardedHeaders.XForwardedHost;
    // Only loopback proxies are allowed by default.
    // Clear that restriction because forwarders are enabled by explicit configuration.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});


var app = builder.Build();

app.UseOpenTelemetryPrometheusScrapingEndpoint();

app.UseForwardedHeaders();

app.UseHttpsRedirection();

var identitySettings =
    builder.Configuration.GetSection(nameof(IdentitySettings)).Get<IdentitySettings>();

// Log the configuration for debugging
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("IdentitySettings PathBase: {PathBase}", identitySettings?.PathBase);
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

// Log all configuration sections for debugging
logger.LogInformation("All configuration keys:");
foreach (var kvp in builder.Configuration.AsEnumerable())
{
    if (kvp.Key.StartsWith("IdentitySettings") || kvp.Key.StartsWith("ServiceSettings"))
    {
        logger.LogInformation("  {Key}: {Value}", kvp.Key, kvp.Value);
    }
}

// Log all environment variables for debugging
logger.LogInformation("All environment variables:");
foreach (var kvp in Environment.GetEnvironmentVariables().Cast<DictionaryEntry>())
{
    if (kvp.Key.ToString().StartsWith("IdentitySettings") || kvp.Key.ToString().StartsWith("ServiceSettings"))
    {
        logger.LogInformation("  {Key}: {Value}", kvp.Key, kvp.Value);
    }
}


//app.Use(async (context, next) =>
//{
//    var logger = app.Services.GetRequiredService<ILogger<Program>>();

//    logger.LogInformation("=== Incoming Request ===");

//    // Basic info
//    logger.LogInformation("Scheme: {Scheme}", context.Request.Scheme);
//    logger.LogInformation("Host: {Host}", context.Request.Host);
//    logger.LogInformation("PathBase: {PathBase}", context.Request.PathBase);
//    logger.LogInformation("Loaded PathBase from configuration: '{PathBase}'", identitySettings?.PathBase);
//    logger.LogInformation("Path: {Path}", context.Request.Path);
//    logger.LogInformation("QueryString: {QueryString}", context.Request.QueryString);
//    logger.LogInformation("Full URL: {Url}", context.Request.GetDisplayUrl());

//    // Headers
//    foreach (var header in context.Request.Headers)
//    {
//        logger.LogInformation("Header: {Key} = {Value}", header.Key, header.Value);
//    }

//    // ��� �� UsePathBase
//    await next();

//    logger.LogInformation("=== End of Request ===");
//});


// Try to get PathBase from multiple sources
var pathBase = identitySettings?.PathBase;
if (string.IsNullOrEmpty(pathBase))
{
    // Try to get from environment variable directly
    pathBase = Environment.GetEnvironmentVariable("IdentitySettings__PathBase");
    logger.LogInformation("PathBase from environment variable: {PathBase}", pathBase);
}

// If still empty, use hardcoded value for production
if (string.IsNullOrEmpty(pathBase) && !app.Environment.IsDevelopment())
{
    pathBase = "/identity-svc";
    logger.LogInformation("Using hardcoded PathBase for production: {PathBase}", pathBase);
}

if (!string.IsNullOrEmpty(pathBase))
{
    logger.LogInformation("Applying PathBase: {PathBase}", pathBase);
    app.UsePathBase(pathBase);
    
    // Add logging to verify PathBase is being applied
    app.Use(async (context, next) =>
    {
        logger.LogInformation("Request PathBase: {RequestPathBase}", context.Request.PathBase);
        logger.LogInformation("Request Path: {RequestPath}", context.Request.Path);
        logger.LogInformation("Full URL: {FullUrl}", context.Request.GetDisplayUrl());
        await next();
    });
}
else
{
    logger.LogWarning("No PathBase configured - this may cause issues in production");
}

app.UseStaticFiles();

app.UseRouting();

app.UseIdentityServer();

app.UseAuthentication();
app.UseAuthorization();

app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Lax
});

app.MapControllers();
app.MapRazorPages();
app.MapCustomHealthChecks();
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors(_builder =>
{
    _builder.WithOrigins(builder.Configuration[AllowedOriginSetting])
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
});
app.Run();




void AddIdentityServer(WebApplicationBuilder webApplicationBuilder)
{
    var identityServerSettings =
        webApplicationBuilder.Configuration.GetSection(nameof(IdentityServerSettings)).Get<IdentityServerSettings>();
    var serverSettings =
        webApplicationBuilder.Configuration.GetSection(nameof(ServiceSettings)).Get<ServiceSettings>();
    var identitySettingsConfig =
        webApplicationBuilder.Configuration.GetSection(nameof(IdentitySettings)).Get<IdentitySettings>();
    
    // Log configuration for debugging
    var logger = webApplicationBuilder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
    logger.LogInformation("ServerSettings Authority: {Authority}", serverSettings?.Authority);
    logger.LogInformation("IdentitySettings PathBase: {PathBase}", identitySettingsConfig?.PathBase);
    
    var _builder = webApplicationBuilder.Services.AddIdentityServer(options =>
        {
            options.Events.RaiseSuccessEvents = true;
            options.Events.RaiseFailureEvents = true;
            options.Events.RaiseErrorEvents = true;
            options.KeyManagement.KeyPath = "/app/keys"; // Use the writable directory
            
            // Configure timeout for endpoints
            options.Endpoints.EnableEndSessionEndpoint = true;
            
            // Set the correct IssuerUri for production with PathBase
            var pathBaseForIssuer = identitySettingsConfig?.PathBase;
            if (string.IsNullOrEmpty(pathBaseForIssuer))
            {
                pathBaseForIssuer = Environment.GetEnvironmentVariable("IdentitySettings__PathBase");
            }
            
            // If still empty, use hardcoded value for production
            if (string.IsNullOrEmpty(pathBaseForIssuer) && !webApplicationBuilder.Environment.IsDevelopment())
            {
                pathBaseForIssuer = "/identity-svc";
                logger.LogInformation("Using hardcoded PathBase for IssuerUri in production: {PathBase}", pathBaseForIssuer);
            }
            
            if (!string.IsNullOrEmpty(pathBaseForIssuer))
            {
                var issuerUri = $"{serverSettings.Authority}{pathBaseForIssuer}";
                options.IssuerUri = issuerUri;
                logger.LogInformation("Setting IssuerUri to: {IssuerUri}", issuerUri);
            }
            else
            {
                options.IssuerUri = serverSettings.Authority;
                logger.LogInformation("Setting IssuerUri to: {IssuerUri}", serverSettings.Authority);
            }
            //options.KeyManagement.Enabled = false;
        })
        .AddAspNetIdentity<ApplicationUser>()
        .AddInMemoryApiScopes(identityServerSettings.ApiScopes)
        .AddInMemoryApiResources(identityServerSettings.ApiResources)
        .AddInMemoryClients(identityServerSettings.Clients)
        .AddInMemoryIdentityResources(identityServerSettings.IdentityResources);

    // Configure additional Identity Server options for production
    if (!webApplicationBuilder.Environment.IsDevelopment())
    {
        webApplicationBuilder.Services.Configure<Duende.IdentityServer.Configuration.IdentityServerOptions>(options =>
        {
            // Ensure endpoints are properly configured with PathBase
            options.Endpoints.EnableDiscoveryEndpoint = true;
            options.Endpoints.EnableTokenEndpoint = true;
            options.Endpoints.EnableUserInfoEndpoint = true;
            options.Endpoints.EnableEndSessionEndpoint = true;
            options.Endpoints.EnableCheckSessionEndpoint = true;
            options.Endpoints.EnableIntrospectionEndpoint = true;
        });
    }

    if (webApplicationBuilder.Environment.IsDevelopment())
    {
        _builder.AddDeveloperSigningCredential();
    }
    else
    {
        var identitySettings =
            webApplicationBuilder.Configuration.GetSection(nameof(IdentitySettings)).Get<IdentitySettings>();
        var cert = X509Certificate2.CreateFromPemFile(
            identitySettings.CertificateCerFilePath,
            identitySettings.CertificateKeyFilePath);
        _builder.AddSigningCredential(cert);
    }
}

void ConfigureDataProtection(WebApplicationBuilder builder)
{
    var dataProtectionSettings = builder.Configuration.GetSection(nameof(DataProtectionSettings)).Get<DataProtectionSettings>();
    
    // Use file system with persistent volume for key storage
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionSettings.FileSystemPath));
    
    // Configure IdentityServer to use the same directory for signing keys
    builder.Services.Configure<Duende.IdentityServer.Configuration.IdentityServerOptions>(options =>
    {
        options.KeyManagement.KeyPath = dataProtectionSettings.FileSystemPath;
    });
}
