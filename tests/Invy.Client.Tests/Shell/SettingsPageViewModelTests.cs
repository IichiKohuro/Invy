using System.Text.Json;
using Invy.Client.Core.Pages;
using Invy.Client.Core.Shell;
using Invy.Client.Core.Updates;
using Invy.Contracts;
using Invy.Contracts.Updates;

namespace Invy.Client.Tests.Shell;

public class SettingsPageViewModelTests
{
    [Theory]
    [InlineData("192.168.1.10", "http://192.168.1.10:5470/")]
    [InlineData("home-pc:8080", "http://home-pc:8080/")]
    [InlineData("http://home-pc", "http://home-pc:5470/")]
    [InlineData("https://invy.local:443", "https://invy.local/")]
    [InlineData("  http://10.0.0.5:5470/  ", "http://10.0.0.5:5470/")]
    public void Server_address_gets_scheme_and_default_port(string input, string expected)
    {
        var settings = new SettingsPageViewModel(new FakeThemeService(), new FakeUpdater(), input);

        settings.TryGetServerUrl(out var url).ShouldBeTrue();
        url.ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ftp://home-pc")]
    [InlineData("http://")]
    public void Invalid_server_address_disables_update_check(string input)
    {
        var settings = new SettingsPageViewModel(new FakeThemeService(), new FakeUpdater(), input);

        settings.CheckForUpdatesCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task Found_update_can_be_installed()
    {
        var updater = new FakeUpdater { Available = "0.2.0" };
        var settings = new SettingsPageViewModel(new FakeThemeService(), updater, "192.168.1.10");

        settings.InstallUpdateCommand.CanExecute(null).ShouldBeFalse();
        await settings.CheckForUpdatesCommand.ExecuteAsync(null);

        updater.CheckedUrl!.ToString().ShouldBe("http://192.168.1.10:5470/");
        settings.ReadyVersion.ShouldBe("0.2.0");
        settings.UpdateStatus.ShouldContain("0.2.0");
        settings.InstallUpdateCommand.CanExecute(null).ShouldBeTrue();

        settings.InstallUpdateCommand.Execute(null);
        updater.Installed.ShouldBeTrue();
    }

    [Fact]
    public async Task Network_error_is_shown_and_does_not_throw()
    {
        var updater = new FakeUpdater { Error = new HttpRequestException("Connection refused") };
        var settings = new SettingsPageViewModel(new FakeThemeService(), updater, "192.168.1.10");

        await settings.CheckForUpdatesCommand.ExecuteAsync(null);

        settings.UpdateStatus.ShouldContain("Connection refused");
        settings.ReadyVersion.ShouldBeNull();
    }

    [Fact]
    public void Unsupported_updater_explains_why()
    {
        var settings = new SettingsPageViewModel(new FakeThemeService(), new FakeUpdater { Supported = false }, "192.168.1.10");

        settings.UpdateStatus.ShouldBe("не установлено");
        settings.CheckForUpdatesCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void Android_manifest_uses_camel_case_json()
    {
        var manifest = new AndroidReleaseManifest(200, "0.2.0", "invy-0.2.0.apk", "ABC123", 1024, "Что нового");

        var json = JsonSerializer.Serialize(manifest, InvyJsonContext.Default.AndroidReleaseManifest);

        // Имена — как в latest.json, который пишет tools/pack-android.ps1.
        json.ShouldStartWith("""{"versionCode":200,"versionName":"0.2.0","apkFileName":"invy-0.2.0.apk","sha256":"ABC123","size":1024,"notes":""");
        JsonSerializer.Deserialize(json, InvyJsonContext.Default.AndroidReleaseManifest).ShouldBe(manifest);
    }

    private sealed class FakeThemeService : IThemeService
    {
        public ThemeMode Mode { get; private set; }

        public void Apply(ThemeMode mode) => Mode = mode;
    }

    private sealed class FakeUpdater : IAppUpdater
    {
        public string? Available { get; init; }

        public Exception? Error { get; init; }

        public bool Supported { get; init; } = true;

        public Uri? CheckedUrl { get; private set; }

        public bool Installed { get; private set; }

        public string CurrentVersion => "0.1.0";

        public bool IsSupported => Supported;

        public string? UnsupportedReason => Supported ? null : "не установлено";

        public Task<string?> CheckAndDownloadAsync(Uri serverUrl, CancellationToken cancellationToken = default)
        {
            CheckedUrl = serverUrl;
            return Error is null ? Task.FromResult(Available) : Task.FromException<string?>(Error);
        }

        public void Install() => Installed = true;
    }
}
