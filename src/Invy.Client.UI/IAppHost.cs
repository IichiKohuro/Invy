using Invy.Client.Core.Shell;
using Invy.Client.Core.Updates;

namespace Invy.Client.UI;

/// <summary>
/// Хост платформы: Desktop создаёт главное окно, трей и горячие клавиши; Android — главный вид.
/// Без хоста <see cref="App"/> ведёт себя по умолчанию (окно или вид в зависимости от платформы).
/// </summary>
public interface IAppHost
{
    /// <summary>Самообновление платформы (Velopack или APK); <c>null</c> — не поддерживается.</summary>
    IAppUpdater? Updater { get; }

    /// <summary>Адрес сервера Invy по умолчанию для настроек.</summary>
    string? DefaultServerAddress { get; }

    void OnFrameworkInitialized(App app, ShellViewModel shell);
}
