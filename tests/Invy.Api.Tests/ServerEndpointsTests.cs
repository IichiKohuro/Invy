using System.Net;
using System.Net.Http.Json;
using Invy.Contracts;
using Invy.Contracts.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Invy.Api.Tests;

public sealed class ServerEndpointsTests : IClassFixture<ServerEndpointsTests.Factory>
{
    private readonly Factory _factory;

    public ServerEndpointsTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Health_returns_healthy()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(ApiRoutes.Health, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("Healthy");
    }

    [Fact]
    public async Task Info_returns_stable_server_identity()
    {
        using var client = _factory.CreateClient();

        var first = await client.GetFromJsonAsync(ApiRoutes.Info, InvyJsonContext.Default.ServerInfoDto, TestContext.Current.CancellationToken);
        var second = await client.GetFromJsonAsync(ApiRoutes.Info, InvyJsonContext.Default.ServerInfoDto, TestContext.Current.CancellationToken);

        first.ShouldNotBeNull();
        first.ApiVersion.ShouldBe(ApiRoutes.ApiVersion);
        first.ServerId.ShouldNotBe(Guid.Empty);
        first.Name.ShouldNotBeNullOrWhiteSpace();
        second!.ServerId.ShouldBe(first.ServerId);
        File.Exists(Path.Combine(_factory.DataPath, "server-id")).ShouldBeTrue();
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public string DataPath { get; } = Path.Combine(Path.GetTempPath(), "invy-tests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("Invy:DataPath", DataPath);

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (Directory.Exists(DataPath))
            {
                Directory.Delete(DataPath, recursive: true);
            }
        }
    }
}
