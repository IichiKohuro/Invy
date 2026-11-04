using Invy.Client.Core.Updates;
using Invy.Contracts;
using Velopack;
using Velopack.Sources;

namespace Invy.Desktop.Updates;

/// <summary>
/// Обновление Desktop через Velopack. Фид раздаёт сервер Invy: <c>http://сервер:5470/updates/win</c>.
/// Дельта-пакет скачивается в фоне; установка — по кнопке «Перезапустить» или при выходе из приложения.
/// </summary>
internal sealed class DesktopUpdateService(Uri defaultServerUrl) : IAppUpdater
{
    private UpdateManager _manager = CreateManager(defaultServerUrl);
    private Uri _managerUrl = defaultServerUrl;
    private UpdateInfo? _pending;

    public string CurrentVersion =>
        _manager.CurrentVersion?.ToString() ?? typeof(DesktopUpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Velopack обновляет только установленное приложение (не запущенное из bin/ при разработке).</summary>
    public bool IsSupported => _manager.IsInstalled;

    public string? UnsupportedReason => IsSupported ? null : "Обновления работают в установленной версии (Setup.exe)";

    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    public async Task<string?> CheckAndDownloadAsync(Uri serverUrl, CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return null;
        }

        if (serverUrl != _managerUrl)
        {
            _manager = CreateManager(serverUrl);
            _managerUrl = serverUrl;
        }

        var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (info is null)
        {
            return null;
        }

        await _manager.DownloadUpdatesAsync(info, _ => { }, cancellationToken).ConfigureAwait(false);
        _pending = info;
        return PendingVersion;
    }

    /// <summary>Установить скачанное обновление и перезапустить приложение (завершает процесс).</summary>
    public void Install()
    {
        if (_pending is not null)
        {
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease, []);
        }
    }

    /// <summary>Установить скачанное обновление после выхода из приложения, без перезапуска.</summary>
    public void ApplyOnExit()
    {
        if (_pending is not null)
        {
            _manager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: true, restart: false, []);
        }
    }

    private static UpdateManager CreateManager(Uri serverUrl) =>
        new(new SimpleWebSource(new Uri(serverUrl, $"{ApiRoutes.Updates}/win/")));
}
