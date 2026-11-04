using Avalonia.Threading;
using Invy.Client.Core.Connection;
using Invy.Client.Core.Session;
using Invy.Client.Core.Shell;
using Invy.Client.UI.Services;
using Invy.Contracts.Devices;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Invy.Client.Tests.Support;

/// <summary>Сервер Invy в памяти («Доверенная сеть») и подключённый к нему сеанс клиента с настоящими диалогами.</summary>
public sealed class TestBackend : WebApplicationFactory<global::Program>
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private readonly string _dataPath = Path.Combine(Path.GetTempPath(), "invy-client-tests", Guid.NewGuid().ToString("N"));

    public ClientSession? Session { get; private set; }

    /// <summary>Окно, над которым показываются диалоги (в headless нет списка окон приложения).</summary>
    public Avalonia.Controls.Window? Host { get; set; }

    /// <summary>Подключиться, добавить стартовый набор и создать сеанс для оболочки.</summary>
    public async Task<ClientSession> ConnectAsync(ShellViewModel shell)
    {
        var settings = new InMemorySettingsStore();
        var connection = new ServerConnection(settings, new PlainTokenProtector(), () => Server.CreateHandler(), "Тест", DevicePlatform.Windows, "0.2.0");
        if (!await connection.ConnectAsync(Server.BaseAddress))
        {
            throw new InvalidOperationException(connection.Error);
        }

        await connection.Api!.ApplyStarterKitAsync();
        Session = new ClientSession(connection, settings, new DialogService(shell, () => Host), AvaloniaUiDispatcher.Instance);
        await Session.Catalog.LoadAsync(Session.Api);
        shell.AttachSession(Session);
        return Session;
    }

    /// <summary>Ждать условия, прокачивая очередь UI (headless-тесты идут в потоке UI).</summary>
    public static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Условие не выполнилось вовремя");
            }

            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Invy:DataPath", _dataPath);
        builder.UseSetting("Invy:Auth:Mode", "TrustedLan");
    }

    public override async ValueTask DisposeAsync()
    {
        if (Session is not null)
        {
            await Session.DisposeAsync();
        }

        await base.DisposeAsync();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }
}
