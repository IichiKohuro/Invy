using Invy.Contracts;
using Velopack;
using Velopack.Sources;

namespace Invy.Desktop.Updates;

/// <summary>
/// Обновление Desktop через Velopack. Фид раздаёт сервер Invy: <c>http://сервер:5470/updates/win</c>.
/// Дельта-пакет скачивается в фоне; установка — по кнопке «Перезапустить» или при выходе из приложения.
/// </summary>
internal sealed class DesktopUpdateService(Uri serverUrl)
{
    private readonly UpdateManager _manager = new(new SimpleWebSource(new Uri(serverUrl, $"{ApiRoutes.Updates}/win/")));
    private UpdateInfo? _pending;

    /// <summary>Velopack обновляет только установленное приложение (не запущенное из bin/ при разработке).</summary>
    public bool IsInstalled => _manager.IsInstalled;

    public string CurrentVersion => _manager.CurrentVersion?.ToString() ?? typeof(DesktopUpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public string? PendingVersion => _pending?.TargetFullRelease.Version.ToString();

    /// <summary>Проверяет фид и скачивает обновление. Возвращает его версию или <c>null</c>, если обновлений нет.</summary>
    public async Task<string?> CheckAndDownloadAsync(CancellationToken cancellationToken = default)
    {
        if (!IsInstalled)
        {
            return null;
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
    public void ApplyAndRestart()
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
}
