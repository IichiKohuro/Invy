using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Api;
using Invy.Client.Core.Session;
using Invy.Contracts.Items;

namespace Invy.Client.Core.Photos;

/// <summary>Фото в ленте: уже на сервере (<see cref="MediaId"/>) или выбрано для новой вещи и ждёт сохранения.</summary>
public sealed partial class PhotoViewModel : ObservableObject
{
    internal PhotoViewModel(MediaDto media)
    {
        MediaId = media.Id;
        FileName = media.FileName;
    }

    internal PhotoViewModel(string fileName, byte[] content)
    {
        FileName = fileName;
        Content = content;
        Thumbnail = content;
    }

    public Guid? MediaId { get; private set; }

    public string FileName { get; }

    /// <summary>Ещё не загружено на сервер (новая вещь).</summary>
    public bool IsPending => MediaId is null;

    internal byte[]? Content { get; private set; }

    /// <summary>Картинка для ленты: превью с сервера или сам файл (для ещё не загруженных).</summary>
    [ObservableProperty]
    public partial byte[]? Thumbnail { get; internal set; }

    [ObservableProperty]
    public partial bool IsCover { get; internal set; }

    internal void Uploaded(MediaDto media)
    {
        MediaId = media.Id;
        Content = null;
        OnPropertyChanged(nameof(MediaId));
        OnPropertyChanged(nameof(IsPending));
    }
}

/// <summary>
/// Фото вещи: добавить (диалог, перетаскивание, вставка из буфера), сделать обложкой, удалить.
/// У существующей вещи изменения сразу уходят на сервер; у новой — копятся и загружаются после сохранения.
/// </summary>
public sealed partial class PhotoStripViewModel : ObservableObject
{
    /// <summary>Сервер принимает до 25 МБ на файл.</summary>
    public const long MaxFileBytes = 25 * 1024 * 1024;

    private readonly ClientSession _session;

    public PhotoStripViewModel(ClientSession session, Guid? itemId = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        ItemId = itemId;
        Photos.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPhotos));
    }

    public Guid? ItemId { get; private set; }

    public ObservableCollection<PhotoViewModel> Photos { get; } = [];

    public bool HasPhotos => Photos.Count > 0;

    public bool CanEdit => _session.CanEdit;

    [ObservableProperty]
    public partial bool IsBusy { get; private set; }

    /// <summary>Фото или обложка изменились на сервере — списки и карточку стоит перечитать.</summary>
    public event Action? Changed;

    public void Load(IReadOnlyList<MediaDto> photos, Guid? coverId)
    {
        ArgumentNullException.ThrowIfNull(photos);
        Photos.Clear();
        foreach (var media in photos)
        {
            var photo = new PhotoViewModel(media) { IsCover = media.Id == coverId };
            Photos.Add(photo);
            _ = LoadThumbnailAsync(photo);
        }
    }

    /// <summary>Добавить файлы: загрузить сразу (есть вещь) или отложить до сохранения новой вещи.</summary>
    public async Task<bool> AddFilesAsync(IReadOnlyList<PickedFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0 || !CanEdit)
        {
            return false;
        }

        IsBusy = true;
        try
        {
            var loaded = new List<(string Name, byte[] Content)>();
            foreach (var file in files)
            {
                await using var stream = await file.OpenAsync();
                if (stream.CanSeek && stream.Length > MaxFileBytes)
                {
                    _session.Dialogs.Notify($"«{file.Name}» больше 25 МБ — пропущено", NotifyKind.Warning);
                    continue;
                }

                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                loaded.Add((file.Name, buffer.ToArray()));
            }

            if (loaded.Count == 0)
            {
                return false;
            }

            if (ItemId is not { } itemId)
            {
                foreach (var (name, content) in loaded)
                {
                    Photos.Add(new PhotoViewModel(name, content) { IsCover = !Photos.Any(p => p.IsCover) });
                }

                return true;
            }

            return await UploadAsync(itemId, loaded.Select(f => (f.Name, f.Content, (PhotoViewModel?)null)).ToList());
        }
        catch (IOException ex)
        {
            _session.Dialogs.Notify($"Не удалось прочитать файл: {ex.Message}", NotifyKind.Error);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Новая вещь сохранена — загрузить отложенные фото (обложка — выбранная или первая).</summary>
    public async Task UploadPendingAsync(Guid itemId)
    {
        ItemId = itemId;
        var pending = Photos.Where(p => p.IsPending && p.Content is not null).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await UploadAsync(itemId, pending.Select(p => (p.FileName, p.Content!, (PhotoViewModel?)p)).ToList());
            if (pending.FirstOrDefault(p => p.IsCover) is { MediaId: { } coverId } cover && !ReferenceEquals(cover, pending[0]))
            {
                await _session.TryAsync(() => _session.Api.SetCoverAsync(itemId, coverId), "Обложка не сохранена");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Фото крупнее — для просмотра.</summary>
    public async Task<byte[]?> LoadPreviewAsync(PhotoViewModel photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        return photo.MediaId is { } id ? await _session.Photos.GetAsync(id, PhotoSize.Preview) : photo.Content;
    }

    [RelayCommand]
    private async Task PickAsync()
    {
        if (CanEdit)
        {
            await AddFilesAsync(await _session.Dialogs.PickImagesAsync());
        }
    }

    [RelayCommand]
    private async Task SetCoverAsync(PhotoViewModel? photo)
    {
        if (photo is null || photo.IsCover || !CanEdit)
        {
            return;
        }

        if (ItemId is { } itemId && photo.MediaId is { } mediaId
            && !await _session.TryAsync(() => _session.Api.SetCoverAsync(itemId, mediaId), "Обложка не сохранена"))
        {
            return;
        }

        foreach (var other in Photos)
        {
            other.IsCover = ReferenceEquals(other, photo);
        }

        if (ItemId is not null)
        {
            Changed?.Invoke();
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(PhotoViewModel? photo)
    {
        if (photo is null || !CanEdit)
        {
            return;
        }

        if (photo.MediaId is { } mediaId)
        {
            if (await _session.Dialogs.AskAsync("Удалить фото?", "Фото удалится с сервера без возможности восстановления.", "Удалить") != DialogChoice.Primary
                || !await _session.TryAsync(() => _session.Api.DeletePhotoAsync(mediaId), "Фото не удалено"))
            {
                return;
            }
        }

        var wasCover = photo.IsCover;
        Photos.Remove(photo);
        if (wasCover && Photos.Count > 0)
        {
            Photos[0].IsCover = true;   // как на сервере: обложкой становится следующее фото
        }

        if (!photo.IsPending)
        {
            Changed?.Invoke();
        }
    }

    private async Task<bool> UploadAsync(Guid itemId, List<(string Name, byte[] Content, PhotoViewModel? Pending)> files)
    {
        IReadOnlyList<MediaDto> uploaded = [];
        var ok = await _session.TryAsync(
            async () => uploaded = await _session.Api.UploadPhotosAsync(itemId, files.Select(f => (f.Name, (Stream)new MemoryStream(f.Content))).ToList()),
            "Фото не загружены");
        if (!ok)
        {
            return false;
        }

        var hadCover = Photos.Any(p => p.IsCover && !p.IsPending);
        for (var i = 0; i < uploaded.Count && i < files.Count; i++)
        {
            var media = uploaded[i];
            var photo = files[i].Pending;
            if (photo is null)
            {
                photo = new PhotoViewModel(media);
                Photos.Add(photo);
            }
            else
            {
                photo.Uploaded(media);
            }

            if (!hadCover && i == 0 && files[i].Pending is null)
            {
                photo.IsCover = !Photos.Any(p => p.IsCover);   // сервер делает обложкой первое фото вещи без обложки
            }

            _ = LoadThumbnailAsync(photo);
        }

        Changed?.Invoke();
        return true;
    }

    private async Task LoadThumbnailAsync(PhotoViewModel photo)
    {
        if (photo.MediaId is { } id && await _session.Photos.GetAsync(id, PhotoSize.Thumb) is { } bytes)
        {
            photo.Thumbnail = bytes;
        }
    }
}
