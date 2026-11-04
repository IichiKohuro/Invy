using Invy.Client.Core.Connection;
using Invy.Client.Core.Shell;
using Invy.Client.Core.Updates;
using Invy.Contracts.Devices;

namespace Invy.Client.UI;

/// <summary>
/// Хост платформы: Desktop создаёт главное окно, трей и горячие клавиши; Android — главный вид.
/// Без хоста <see cref="App"/> ведёт себя по умолчанию (окно или вид в зависимости от платформы).
/// </summary>
public interface IAppHost
{
    /// <summary>Самообновление платформы (Velopack или APK); <c>null</c> — не поддерживается.</summary>
    IAppUpdater? Updater { get; }

    /// <summary>Адрес сервера Invy по умолчанию (если ещё ни разу не подключались).</summary>
    string? DefaultServerAddress { get; }

    /// <summary>Настройки клиента: сервер, токен (защищённый), тема, недавние типы и места.</summary>
    ISettingsStore Settings { get; }

    /// <summary>Шифрование токена устройства: Windows — DPAPI, Android — Keystore.</summary>
    ITokenProtector TokenProtector { get; }

    DevicePlatform Platform { get; }

    /// <summary>Имя устройства для запроса подключения («Ноутбук Маши»).</summary>
    string DeviceName { get; }

    string AppVersion { get; }

    /// <summary>Каталог кэша фото на диске; <c>null</c> — только в памяти.</summary>
    string? PhotoCacheDirectory { get; }

    void OnFrameworkInitialized(App app, ShellViewModel shell);
}
