using Invy.Api.Hosting;
using Invy.Api.Updates;
using Invy.Application;
using Invy.Contracts;
using Invy.Contracts.Server;
using Invy.Infrastructure;
using Microsoft.Extensions.Hosting.WindowsServices;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Служба Windows стартует с текущим каталогом System32 — берём каталог программы.
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
});

builder.Services.AddWindowsService(o => o.ServiceName = "InvyServer");
builder.Services.Configure<InvyServerOptions>(builder.Configuration.GetSection(InvyServerOptions.SectionName));
builder.Services.AddSingleton<ServerIdentity>();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.TypeInfoResolverChain.Insert(0, InvyJsonContext.Default));
builder.Services.AddInvyApplication().AddInvyInfrastructure();

var app = builder.Build();

app.UseExceptionHandler();
app.UseUpdateFeed(app.Services.GetRequiredService<ServerIdentity>().DataPath);
app.MapHealthChecks(ApiRoutes.Health);
app.MapGet(ApiRoutes.Info, (ServerIdentity identity) => new ServerInfoDto(
        identity.ServerId,
        identity.Name,
        ServerIdentity.Version,
        ApiRoutes.ApiVersion,
        MinClientVersion: "0.1.0"))
    .WithName("GetServerInfo");

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

await app.RunAsync();

/// <summary>Точка входа; публичная — для WebApplicationFactory в тестах.</summary>
public partial class Program;
