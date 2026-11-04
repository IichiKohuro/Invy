using System.Net;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace Invy.Api.Tests;

/// <summary>
/// Спайк фазы 0: сервер Invy раздаёт фид Velopack, а настоящий клиент Velopack
/// (<see cref="SimpleWebSource"/>) читает его и скачивает пакеты.
/// Формат <c>releases.win.json</c> — как его создаёт <c>vpk pack</c> 1.2.
/// </summary>
public sealed class UpdateFeedTests : IClassFixture<UpdateFeedTests.Factory>
{
    private readonly Factory _factory;

    public UpdateFeedTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Velopack_client_reads_feed_and_downloads_delta()
    {
        using var client = _factory.CreateClient();
        var source = new SimpleWebSource(new Uri(client.BaseAddress!, "/updates/win/"), new TestServerDownloader(client), 30);
        var log = new NullVelopackLogger();

        var feed = await source.GetReleaseFeed(log, "Invy.Desktop", "win", null, null!);

        feed.Assets.Select(a => (a.Version.ToString(), a.Type)).ShouldBe(
            [("0.2.0", VelopackAssetType.Full), ("0.2.0", VelopackAssetType.Delta)], ignoreOrder: true);

        var delta = feed.Assets.Single(a => a.Type == VelopackAssetType.Delta);
        var target = Path.Combine(_factory.DataPath, "downloaded.nupkg");
        await source.DownloadReleaseEntry(log, delta, target, _ => { }, TestContext.Current.CancellationToken);

        Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken))).ShouldBe(delta.SHA256);
    }

    [Theory]
    [InlineData("/updates/win/releases.win.json", "application/json", "no-cache")]
    [InlineData("/updates/win/Invy.Desktop-0.2.0-delta.nupkg", "application/octet-stream", "public, max-age=31536000, immutable")]
    [InlineData("/updates/win/Invy.Desktop-win-Setup.exe", "application/vnd.microsoft.portable-executable", "no-cache")]
    [InlineData("/updates/android/latest.json", "application/json", "no-cache")]
    [InlineData("/updates/android/invy-0.2.0.apk", "application/vnd.android.package-archive", "public, max-age=31536000, immutable")]
    public async Task Serves_files_with_content_type_and_cache_policy(string path, string contentType, string cacheControl)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(contentType);
        response.Headers.CacheControl!.ToString().ShouldBe(cacheControl);
    }

    [Theory]
    [InlineData("/updates/win/RELEASES")]                 // неизвестные типы файлов не отдаются
    [InlineData("/updates/win/../../server-id")]          // выход за пределы каталога релизов
    [InlineData("/updates/win/%2e%2e/%2e%2e/server-id")]
    public async Task Does_not_serve_other_files(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public Factory()
        {
            var win = Directory.CreateDirectory(Path.Combine(DataPath, "releases", "win")).FullName;
            var full = WritePackage(win, "Invy.Desktop-0.2.0-full.nupkg", "full package");
            var delta = WritePackage(win, "Invy.Desktop-0.2.0-delta.nupkg", "delta package");
            File.WriteAllText(Path.Combine(win, "Invy.Desktop-win-Setup.exe"), "setup");
            File.WriteAllText(Path.Combine(win, "RELEASES"), "legacy");
            File.WriteAllText(Path.Combine(win, "releases.win.json"), $$"""
                {"Assets":[{{Asset("Full", full)}},{{Asset("Delta", delta)}}]}
                """);
            File.WriteAllText(Path.Combine(DataPath, "server-id"), Guid.NewGuid().ToString());

            var android = Directory.CreateDirectory(Path.Combine(DataPath, "releases", "android")).FullName;
            File.WriteAllText(Path.Combine(android, "invy-0.2.0.apk"), "apk");
            File.WriteAllText(Path.Combine(android, "latest.json"), """{"versionCode":200,"versionName":"0.2.0","apkFileName":"invy-0.2.0.apk","sha256":"00","size":3}""");
        }

        public string DataPath { get; } = Path.Combine(Path.GetTempPath(), "invy-tests", Guid.NewGuid().ToString("N"));

        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("Invy:DataPath", DataPath);

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            Directory.Delete(DataPath, recursive: true);
        }

        private static FileInfo WritePackage(string directory, string name, string content)
        {
            var path = Path.Combine(directory, name);
            File.WriteAllText(path, content);
            return new FileInfo(path);
        }

        private static string Asset(string type, FileInfo file)
        {
            var bytes = File.ReadAllBytes(file.FullName);
#pragma warning disable CA5350 // SHA1 — поле формата фида Velopack, проверка целостности идёт по SHA256
            var sha1 = Convert.ToHexString(SHA1.HashData(bytes));
#pragma warning restore CA5350
            return $$"""
                {"PackageId":"Invy.Desktop","Version":"0.2.0","Type":"{{type}}","FileName":"{{file.Name}}","SHA1":"{{sha1}}","SHA256":"{{Convert.ToHexString(SHA256.HashData(bytes))}}","Size":{{bytes.Length}}}
                """;
        }
    }

    /// <summary>Загрузчик Velopack поверх HttpClient тестового сервера.</summary>
    private sealed class TestServerDownloader(HttpClient client) : IFileDownloader
    {
        public async Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers, double timeout, CancellationToken cancelToken)
        {
            await File.WriteAllBytesAsync(targetFile, await client.GetByteArrayAsync(url, cancelToken), cancelToken);
            progress(100);
        }

        public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers, double timeout) => client.GetByteArrayAsync(url);

        public Task<string> DownloadString(string url, IDictionary<string, string>? headers, double timeout) => client.GetStringAsync(url);
    }
}
