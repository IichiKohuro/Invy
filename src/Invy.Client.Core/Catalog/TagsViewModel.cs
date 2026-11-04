using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Invy.Client.Core.Explorer;
using Invy.Client.Core.Session;
using Invy.Client.Core.Shell;
using Invy.Contracts.Catalog;

namespace Invy.Client.Core.Catalog;

/// <summary>Объединить тег с другим: вещи получают <see cref="Target"/>, исходный тег удаляется.</summary>
public sealed record TagMergeOption(TagRowViewModel Source, TagDto Target)
{
    public string Title => $"#{Target.Name}";
}

/// <summary>Тег в списке: название (правится на месте), цвет, сколько вещей.</summary>
public sealed partial class TagRowViewModel : ObservableObject
{
    public TagRowViewModel(TagDto tag, IEnumerable<TagDto> others)
    {
        Tag = tag;
        EditName = tag.Name;
        MergeOptions = others.Where(t => t.Id != tag.Id).Select(t => new TagMergeOption(this, t)).ToList();
    }

    public TagDto Tag { get; }

    public string Name => Tag.Name;

    public string? Color => Tag.Color;

    public string CountText => Tag.ItemCount == 0 ? "нет вещей" : ItemVisuals.Things(Tag.ItemCount);

    public IReadOnlyList<TagMergeOption> MergeOptions { get; }

    public bool CanMerge => MergeOptions.Count > 0;

    [ObservableProperty]
    public partial string EditName { get; set; }

    [ObservableProperty]
    public partial bool IsEditing { get; set; }
}

/// <summary>Страница «Теги»: создать, переименовать, цвет, объединить, удалить.</summary>
public sealed partial class TagsViewModel : PageViewModel, IDisposable
{
    private readonly ClientSession _session;

    public TagsViewModel(ClientSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
        session.Catalog.Changed += Refresh;
        Refresh();
    }

    public override string Title => "Теги";

    public bool CanEdit => _session.CanEdit;

    public ObservableCollection<TagRowViewModel> Tags { get; } = [];

    public bool IsEmpty => Tags.Count == 0;

    [ObservableProperty]
    public partial string? NewTagName { get; set; }

    [ObservableProperty]
    public partial string? Error { get; set; }

    public void Dispose() => _session.Catalog.Changed -= Refresh;

    public override async Task ActivateAsync()
    {
        // Счётчики вещей меняются при каждом изменении вещей — освежить при открытии.
        await _session.TryAsync(() => _session.Catalog.LoadAsync(_session.Api));
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        Error = null;
        var name = NewTagName?.Trim().TrimStart('#');
        if (string.IsNullOrEmpty(name) || !CanEdit)
        {
            return;
        }

        try
        {
            await _session.Api.CreateTagAsync(new SaveTagRequest(name, SortOrder: Tags.Count));
            NewTagName = null;
            await _session.Catalog.LoadAsync(_session.Api);
        }
        catch (Api.ApiException ex)
        {
            Error = ex.UserMessage;
        }
        catch (HttpRequestException)
        {
            Error = "Нет связи с сервером Invy";
        }
    }

    [RelayCommand]
    private void BeginRename(TagRowViewModel? row)
    {
        if (row is not null && CanEdit)
        {
            row.EditName = row.Name;
            row.IsEditing = true;
        }
    }

    [RelayCommand]
    private async Task RenameAsync(TagRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.IsEditing = false;
        var name = row.EditName.Trim().TrimStart('#');
        if (name.Length == 0 || name == row.Name)
        {
            row.EditName = row.Name;
            return;
        }

        if (await Save(row, name, row.Color))
        {
            _session.Dialogs.Notify($"#{row.Name} → #{name}", NotifyKind.Success);
        }
    }

    [RelayCommand]
    private void CancelRename(TagRowViewModel? row)
    {
        if (row is not null && Tags.Contains(row))
        {
            row.IsEditing = false;
            row.EditName = row.Name;
        }
    }

    /// <summary>Параметр — «#RRGGBB» или пусто (без цвета); выбранный тег — <see cref="ColorTarget"/>.</summary>
    [RelayCommand]
    private async Task SetColorAsync(string? color)
    {
        if (ColorTarget is { } row)
        {
            await Save(row, row.Name, string.IsNullOrEmpty(color) ? null : color);
        }
    }

    /// <summary>Тег, которому выбирают цвет (открыто меню цвета).</summary>
    [ObservableProperty]
    public partial TagRowViewModel? ColorTarget { get; set; }

    [RelayCommand]
    private async Task MergeAsync(TagMergeOption? option)
    {
        if (option is null || !CanEdit)
        {
            return;
        }

        var (source, target) = (option.Source, option.Target);
        if (await _session.Dialogs.AskAsync(
                $"Объединить #{source.Name} с #{target.Name}?",
                $"Вещи с тегом #{source.Name} ({source.CountText}) получат тег #{target.Name}, а #{source.Name} будет удалён.",
                "Объединить") != DialogChoice.Primary)
        {
            return;
        }

        if (await _session.TryAsync(() => _session.Api.MergeTagAsync(source.Tag.Id, target.Id), "Теги не объединены"))
        {
            _session.Dialogs.Notify($"#{source.Name} объединён с #{target.Name}", NotifyKind.Success);
            await _session.Catalog.LoadAsync(_session.Api);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync(TagRowViewModel? row)
    {
        if (row is null || !CanEdit
            || await _session.Dialogs.AskAsync($"Удалить тег #{row.Name}?", $"Тег снимется с вещей ({row.CountText}). Сами вещи останутся.", "Удалить") != DialogChoice.Primary)
        {
            return;
        }

        if (await _session.TryAsync(() => _session.Api.DeleteTagAsync(row.Tag.Id), "Тег не удалён"))
        {
            await _session.Catalog.LoadAsync(_session.Api);
        }
    }

    private async Task<bool> Save(TagRowViewModel row, string name, string? color)
    {
        var ok = await _session.TryAsync(
            () => _session.Api.UpdateTagAsync(row.Tag.Id, new SaveTagRequest(name, color, row.Tag.SortOrder, row.Tag.Version)),
            $"#{row.Name}");
        if (ok)
        {
            await _session.Catalog.LoadAsync(_session.Api);
        }
        else
        {
            row.EditName = row.Name;
        }

        return ok;
    }

    private void Refresh()
    {
        var tags = _session.Catalog.Tags.OrderBy(t => t.SortOrder).ThenBy(t => t.Name, StringComparer.CurrentCulture).ToList();
        Tags.Clear();
        foreach (var tag in tags)
        {
            Tags.Add(new TagRowViewModel(tag, tags));
        }

        OnPropertyChanged(nameof(IsEmpty));
    }
}
