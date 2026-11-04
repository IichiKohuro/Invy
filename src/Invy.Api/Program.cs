using Invy.Api.Endpoints;
using Invy.Api.Hosting;
using Invy.Api.Realtime;
using Invy.Api.Updates;
using Invy.Application;
using Invy.Contracts;
using Invy.Contracts.Devices;
using Invy.Contracts.Server;
using Invy.Infrastructure;
using Invy.Infrastructure.Persistence;
using Microsoft.Extensions.Hosting.WindowsServices;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Служба Windows стартует с текущим каталогом System32 — берём каталог программы.
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
});

var serverOptions = builder.Configuration.GetSection(InvyServerOptions.SectionName).Get<InvyServerOptions>() ?? new InvyServerOptions();
var dataPath = serverOptions.ResolveDataPath(builder.Environment.ContentRootPath);

builder.Logging.AddInvyFileLogging(dataPath);
builder.Services.AddWindowsService(o => o.ServiceName = "InvyServer");
builder.Services.Configure<InvyServerOptions>(builder.Configuration.GetSection(InvyServerOptions.SectionName));
builder.Services.AddSingleton<ServerIdentity>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, InvyJsonContext.Default));
builder.Services.AddInvyAuth(builder.Configuration);
builder.Services.AddPairingRateLimit();
builder.Services.AddInvyRealtime();
builder.Services.AddInvyApplication().AddInvyInfrastructure(dataPath);

var app = builder.Build();

await DatabaseMigrator.MigrateAsync(app.Services, Path.Combine(dataPath, "backups"));

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseUpdateFeed(dataPath);   // релизы клиентов раздаются без авторизации — до UseAuthentication
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks(ApiRoutes.Health).AllowAnonymous();
app.MapGet(ApiRoutes.Info, (ServerIdentity identity) => new ServerInfoDto(
        identity.ServerId,
        identity.Name,
        ServerIdentity.Version,
        ApiRoutes.ApiVersion,
        MinClientVersion: "0.1.0"))
    .WithName("GetServerInfo")
    .AllowAnonymous();
app.MapCatalog();
app.MapItems();
app.MapDevices();
app.MapMedia();
app.MapHub<InventoryHub>(HubRoutes.Inventory);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

await app.RunAsync();

/// <summary>Точка входа; публичная — для WebApplicationFactory в тестах.</summary>
public partial class Program;
