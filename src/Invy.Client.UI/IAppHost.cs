using Invy.Client.Core.Shell;

namespace Invy.Client.UI;

/// <summary>
/// Хост платформы: Desktop создаёт главное окно, трей и горячие клавиши; Android — главный вид.
/// Без хоста <see cref="App"/> ведёт себя по умолчанию (окно или вид в зависимости от платформы).
/// </summary>
public interface IAppHost
{
    void OnFrameworkInitialized(App app, ShellViewModel shell);
}
