using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Shell;
using Invy.Client.Core.Updates;

namespace Invy.Client.Core.Pages;

public sealed partial class SettingsPageViewModel : PageViewModel
{
    private readonly IThemeService _themeService;
    private readonly IAppUpdater? _updater;

    public SettingsPageViewModel(IThemeService themeService, IAppUpdater? updater = null, string? serverAddress = null)
    {
        _themeService = themeService;
        _updater = updater;
        ThemeIndex = (int)themeService.Mode;
        ServerAddress = serverAddress ?? string.Empty;
        UpdateStatus = updater switch
        {
            null => string.Empty,
            { IsSupported: false } => updater.UnsupportedReason ?? "Обновления недоступны",
            _ => $"Установлена версия {updater.CurrentVersion}",
        };
    }

    public override string Title => "Настройки";

    /// <summary>Порядок совпадает с <see cref="ThemeMode"/>.</summary>
    public IReadOnlyList<string> ThemeOptions { get; } = ["Как в системе", "Светлая", "Тёмная"];

    [ObservableProperty]
    public partial int ThemeIndex { get; set; }

    /// <summary>Адрес сервера Invy. В фазе 0 — вводится вручную; с фазы 2 — автообнаружение и сохранение.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    public partial string ServerAddress { get; set; }

    [ObservableProperty]
    public partial string UpdateStatus { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallUpdateCommand))]
    public partial string? ReadyVersion { get; private set; }

    public bool HasUpdater => _updater is not null;

    partial void OnThemeIndexChanged(int value)
    {
        if (Enum.IsDefined((ThemeMode)value))
        {
            _themeService.Apply((ThemeMode)value);
        }
    }

    private bool CanCheckForUpdates() => _updater is { IsSupported: true } && TryGetServerUrl(out _);

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync(CancellationToken cancellationToken)
    {
        if (_updater is null || !TryGetServerUrl(out var serverUrl))
        {
            return;
        }

        UpdateStatus = "Проверка обновлений…";
        try
        {
            ReadyVersion = await _updater.CheckAndDownloadAsync(serverUrl, cancellationToken);
            UpdateStatus = ReadyVersion is null
                ? $"Установлена последняя версия ({_updater.CurrentVersion})"
                : $"Версия {ReadyVersion} загружена и готова к установке";
        }
        catch (OperationCanceledException)
        {
            UpdateStatus = "Проверка отменена";
        }
#pragma warning disable CA1031 // ошибка сети или сервера показывается пользователю, приложение продолжает работать
        catch (Exception ex)
#pragma warning restore CA1031
        {
            UpdateStatus = $"Не удалось проверить обновления: {ex.Message}";
        }
    }

    private bool CanInstallUpdate() => ReadyVersion is not null;

    [RelayCommand(CanExecute = nameof(CanInstallUpdate))]
    private void InstallUpdate() => _updater?.Install();

    /// <summary>«192.168.1.10» → <c>http://192.168.1.10:5470/</c>; явно указанные схема и порт сохраняются.</summary>
    internal bool TryGetServerUrl(out Uri url)
    {
        var text = ServerAddress.Trim();
        if (text.Length > 0 && !text.Contains("://", StringComparison.Ordinal))
        {
            text = "http://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out url!) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        var authority = text[(text.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/')[0];
        if (!authority.Contains(':', StringComparison.Ordinal))
        {
            url = new UriBuilder(url) { Port = Contracts.ApiRoutes.DefaultHttpPort }.Uri;
        }

        return true;
    }
}
