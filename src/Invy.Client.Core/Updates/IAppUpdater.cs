namespace Invy.Client.Core.Updates;

/// <summary>
/// Самообновление клиента из локальной сети. Desktop — Velopack, Android — установка APK.
/// Релизы раздаёт сервер Invy: <c>{сервер}/updates/{платформа}/</c>.
/// </summary>
public interface IAppUpdater
{
    /// <summary>Установленная версия приложения.</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// Можно ли обновляться в текущем запуске. Desktop, запущенный из bin/ при разработке, — нельзя;
    /// причина — в <see cref="UnsupportedReason"/>.
    /// </summary>
    bool IsSupported { get; }

    string? UnsupportedReason { get; }

    /// <summary>Проверяет сервер и скачивает обновление. Возвращает его версию или <c>null</c>, если обновлений нет.</summary>
    Task<string?> CheckAndDownloadAsync(Uri serverUrl, CancellationToken cancellationToken = default);

    /// <summary>Устанавливает скачанное обновление: Desktop перезапускается, Android показывает системную установку.</summary>
    void Install();
}
