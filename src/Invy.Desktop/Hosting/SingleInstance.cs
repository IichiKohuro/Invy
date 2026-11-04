using System.IO.Pipes;

namespace Invy.Desktop.Hosting;

/// <summary>
/// Один экземпляр Desktop-приложения на пользователя: именованный Mutex + named pipe.
/// Повторный запуск (ярлык, автозапуск, <c>Invy.Desktop.exe --search "кабель"</c>) передаёт свои
/// аргументы работающему экземпляру и завершается. Pipe доступен только текущему пользователю.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _stop = new();
    private Task? _listener;

    private SingleInstance(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    /// <summary>Аргументы командной строки, присланные повторным запуском. Вызывается в фоновом потоке.</summary>
    public event Action<string[]>? ArgumentsReceived;

    /// <summary>Возвращает владельца или <c>null</c>, если приложение уже запущено.</summary>
    public static SingleInstance? TryAcquire(string appId)
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName(appId), out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new SingleInstance(mutex, PipeName(appId));
    }

    /// <summary>Передаёт аргументы работающему экземпляру. false — не удалось связаться.</summary>
    public static async Task<bool> SendAsync(string appId, string[] args, TimeSpan timeout)
    {
        try
        {
            await using var client = new NamedPipeClientStream(".", PipeName(appId), PipeDirection.Out, PipeOptions.CurrentUserOnly);
            using var cts = new CancellationTokenSource(timeout);
            await client.ConnectAsync(cts.Token).ConfigureAwait(false);

            await using var writer = new BinaryWriter(client);
            writer.Write(args.Length);
            foreach (var arg in args)
            {
                writer.Write(arg);
            }

            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void StartListening() => _listener ??= Task.Run(ListenAsync);

    public void Dispose()
    {
        _stop.Cancel();
        try
        {
            _listener?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // остановка слушателя — ожидаемая отмена
        }

        _stop.Dispose();
        _mutex.Dispose();
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);

                using var reader = new BinaryReader(server);
                var count = reader.ReadInt32();
                if (count is < 0 or > 64)
                {
                    continue;
                }

                var args = new string[count];
                for (var i = 0; i < count; i++)
                {
                    args[i] = reader.ReadString();
                }

                ArgumentsReceived?.Invoke(args);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                // клиент отключился посреди сообщения или прислал неполные данные — ждём следующего
            }
        }
    }

    // Имена на пользователя: у каждого пользователя Windows — свой экземпляр.
    private static string MutexName(string appId) => $"{appId}.{Environment.UserName}.instance";

    private static string PipeName(string appId) => $"{appId}.{Environment.UserName}.pipe";
}
