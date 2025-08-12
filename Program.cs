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
using GreenPipes;
using Common.Library.HealthChecks;
using Identity.Service;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.ConfigureAzureKeyVault(builder.Environment);


const string AllowedOriginSetting = "AllowedOrigin";
//var connectionString = builder.Configuration.GetConnectionString("IdentityDataContextConnection") ?? throw new InvalidOperationException("Connection string 'IdentityDataContextConnection' not found.");;

//builder.Services.AddDbContext<IdentityDataContext>(options => options.UseSqlServer(connectionString));

//builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = true).AddEntityFrameworkStores<IdentityDataContext>();

// Add services to the container.
BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));

var serviceSettings = builder.Configuration.GetSection(nameof(ServiceSettings)).Get<ServiceSettings>();
var mongoDbSettings = builder.Configuration.GetSection(nameof(MongoDbSettings)).Get<MongoDbSettings>();
var identityServerSettings =
    builder.Configuration.GetSection(nameof(IdentityServerSettings)).Get<IdentityServerSettings>();

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



builder.Services.AddIdentityServer(options =>
    {
        options.Events.RaiseSuccessEvents = true;
        options.Events.RaiseFailureEvents = true;
        options.Events.RaiseErrorEvents = true;

        options.KeyManagement.Enabled = false;
    })
    .AddAspNetIdentity<ApplicationUser>()
    .AddInMemoryApiScopes(identityServerSettings.ApiScopes)
    .AddInMemoryApiResources(identityServerSettings.ApiResources)
    .AddInMemoryClients(identityServerSettings.Clients)
    .AddInMemoryIdentityResources(identityServerSettings.IdentityResources)
    .AddDeveloperSigningCredential();

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


builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor |
                               ForwardedHeaders.XForwardedProto;
    // Only loopback proxies are allowed by default.
    // Clear that restriction because forwarders are enabled by explicit configuration.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});





var app = builder.Build();

app.UseForwardedHeaders();

// Configure the HTTP request pipeline.

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseCors(_builder =>
    {
        _builder.WithOrigins(builder.Configuration[AllowedOriginSetting])
            .AllowAnyHeader()
            .AllowAnyMethod();
    });

}


//app.UseHttpsRedirection();
app.UseCookiePolicy(new CookiePolicyOptions
{
    MinimumSameSitePolicy = SameSiteMode.Lax
});

if (app.Environment.IsDevelopment() || app.Environment.IsStaging() || app.Environment.IsProduction())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.Use((context, next) =>
{
    var identitySettings =
        builder.Configuration.GetSection(nameof(IdentitySettings)).Get<IdentitySettings>();
    context.Request.PathBase = new PathString(identitySettings.PathBase);
    return next();
});

app.UseStaticFiles();

app.UseAuthorization();

app.UseIdentityServer();

app.MapControllers();
app.MapRazorPages();
app.MapCustomHealthChecks();


app.Run();
