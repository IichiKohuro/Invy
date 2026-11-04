using System.Collections.Concurrent;
using Invy.Client.Core.Api;

namespace Invy.Client.Core.Photos;

/// <summary>
/// Кэш фото: в памяти (последние <see cref="MemoryLimit"/> штук) и на диске. Файл по id не меняется
/// (новое фото — новый id), поэтому кэш не устаревает. Одновременные запросы одного фото объединяются.
/// </summary>
public sealed class PhotoCache(Func<InvyApiClient> api, string? directory = null)
{
    private readonly ConcurrentDictionary<(Guid, PhotoSize), Task<byte[]?>> _loading = new();
    private readonly LinkedList<(Guid Id, PhotoSize Size, byte[] Bytes)> _recent = new();
    private readonly Dictionary<(Guid, PhotoSize), LinkedListNode<(Guid Id, PhotoSize Size, byte[] Bytes)>> _index = [];
    private readonly Lock _lock = new();

    /// <summary>Сколько фото держать в памяти.</summary>
    public int MemoryLimit { get; init; } = 400;

    /// <summary>Фото нужного размера или <c>null</c>, если загрузить не удалось (нет связи, удалено).</summary>
    public Task<byte[]?> GetAsync(Guid mediaId, PhotoSize size)
    {
        lock (_lock)
        {
            if (_index.TryGetValue((mediaId, size), out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                return Task.FromResult<byte[]?>(node.Value.Bytes);
            }
        }

        var task = _loading.GetOrAdd((mediaId, size), key => LoadAsync(key.Item1, key.Item2));
        if (task.IsCompleted)
        {
            _loading.TryRemove(KeyValuePair.Create((mediaId, size), task));   // завершилась сразу — не держать в «загружаемых»
        }

        return task;
    }

    /// <summary>Положить в кэш то, что уже есть на клиенте (например, только что загруженное превью).</summary>
    public void Put(Guid mediaId, PhotoSize size, byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        lock (_lock)
        {
            if (_index.TryGetValue((mediaId, size), out var existing))
            {
                _recent.Remove(existing);
            }

            _index[(mediaId, size)] = _recent.AddFirst((mediaId, size, bytes));
            while (_recent.Count > MemoryLimit && _recent.Last is { } last)
            {
                _recent.RemoveLast();
                _index.Remove((last.Value.Id, last.Value.Size));
            }
        }
    }

    private async Task<byte[]?> LoadAsync(Guid mediaId, PhotoSize size)
    {
        try
        {
            var path = directory is null ? null : Path.Combine(directory, $"{mediaId:N}-{size.ToString().ToLowerInvariant()}");
            if (path is not null && File.Exists(path))
            {
                var cached = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                Put(mediaId, size, cached);
                return cached;
            }

            var bytes = await api().GetPhotoAsync(mediaId, size).ConfigureAwait(false);
            Put(mediaId, size, bytes);
            if (path is not null)
            {
                await SaveToDiskAsync(path, bytes).ConfigureAwait(false);
            }

            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or ApiException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;   // покажется иконка вместо фото; следующий запрос попробует снова
        }
        finally
        {
            _loading.TryRemove((mediaId, size), out _);
        }
    }

    private static async Task SaveToDiskAsync(string path, byte[] bytes)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes).ConfigureAwait(false);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // диск недоступен — работаем только с памятью
        }
    }
}
