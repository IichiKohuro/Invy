namespace Invy.Desktop.Hosting;

/// <summary>
/// Аргументы командной строки Desktop-приложения:
/// <c>--background</c> — запуск в трей без окна (автозапуск),
/// <c>--quick-search</c> — открыть быстрый поиск,
/// <c>--search "запрос"</c> — открыть поиск с запросом.
/// </summary>
public sealed record DesktopArguments(bool Background, bool QuickSearch, string? Search)
{
    public const string BackgroundFlag = "--background";
    public const string QuickSearchFlag = "--quick-search";
    public const string SearchOption = "--search";

    public static DesktopArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var background = false;
        var quickSearch = false;
        string? search = null;

        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case BackgroundFlag:
                    background = true;
                    break;
                case QuickSearchFlag:
                    quickSearch = true;
                    break;
                case SearchOption when i + 1 < args.Count:
                    search = args[++i];
                    break;
            }
        }

        return new DesktopArguments(background, quickSearch, string.IsNullOrWhiteSpace(search) ? null : search.Trim());
    }
}
