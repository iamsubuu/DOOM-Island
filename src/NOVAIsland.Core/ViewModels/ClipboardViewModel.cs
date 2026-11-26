using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;

namespace NOVAIsland.Core.ViewModels;

public enum ClipboardFilterKind
{
    All,
    Pinned,
    Text,
    Code,
    Link,
    Image
}

public sealed partial class ClipboardViewModel : ObservableObject
{
    private readonly IClipboardRepository _repository;
    private readonly IClipboardMonitorService _monitor;
    private readonly List<ClipboardItem> _allItems = [];

    public ObservableCollection<ClipboardItem> FilteredItems { get; } = [];

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private ClipboardFilterKind _activeFilter = ClipboardFilterKind.All;

    [ObservableProperty]
    private string? _recentlyCopiedId;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _pinnedCount;

    public ClipboardViewModel(IClipboardRepository repository, IClipboardMonitorService monitor)
    {
        _repository = repository;
        _monitor = monitor;
    }

    partial void OnSearchQueryChanged(string value) => RebuildFiltered();
    partial void OnActiveFilterChanged(ClipboardFilterKind value) => RebuildFiltered();

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var items = await _repository.GetAllAsync(cancellationToken);
        _allItems.Clear();
        _allItems.AddRange(items);
        RebuildFiltered();
    }

    public async Task RecordCapturedItemAsync(ClipboardItem item, int historyLimit, CancellationToken cancellationToken = default)
    {
        await _repository.UpsertAsync(item, historyLimit, cancellationToken);
        await LoadAsync(cancellationToken);
    }

    [RelayCommand]
    public void SetFilter(ClipboardFilterKind filter) => ActiveFilter = filter;

    [RelayCommand]
    public async Task CopyItemAsync(ClipboardItem? item)
    {
        if (item is null)
        {
            return;
        }

        await _monitor.CopyToClipboardAsync(item);
        RecentlyCopiedId = item.Id;
    }

    [RelayCommand]
    public async Task TogglePinAsync(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        var existing = _allItems.FirstOrDefault(x => x.Id == id);
        if (existing is null)
        {
            return;
        }

        var nextPinned = !existing.Pinned;
        await _repository.SetPinnedAsync(id, nextPinned);
        var idx = _allItems.IndexOf(existing);
        _allItems[idx] = existing with { Pinned = nextPinned };
        RebuildFiltered();
    }

    [RelayCommand]
    public async Task DeleteItemAsync(string? id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return;
        }

        await _repository.DeleteAsync(id);
        _allItems.RemoveAll(x => x.Id == id);
        RebuildFiltered();
    }

    [RelayCommand]
    public async Task ClearUnpinnedAsync()
    {
        await _repository.ClearAsync(keepPinned: true);
        _allItems.RemoveAll(x => !x.Pinned);
        RebuildFiltered();
    }

    private void RebuildFiltered()
    {
        var query = SearchQuery.Trim();
        var filtered = _allItems.Where(item =>
        {
            var passesType = ActiveFilter switch
            {
                ClipboardFilterKind.Pinned => item.Pinned,
                ClipboardFilterKind.Text => item.Type == ClipboardItemType.Text,
                ClipboardFilterKind.Code => item.Type == ClipboardItemType.Code,
                ClipboardFilterKind.Link => item.Type == ClipboardItemType.Link,
                ClipboardFilterKind.Image => item.Type == ClipboardItemType.Image,
                _ => true
            };

            if (!passesType)
            {
                return false;
            }

            if (string.IsNullOrEmpty(query))
            {
                return true;
            }

            return item.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (item.PreviewTitle?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || item.SourceApp.Contains(query, StringComparison.OrdinalIgnoreCase);
        })
        .OrderByDescending(i => i.Pinned)
        .ThenByDescending(i => i.CapturedAtUtc)
        .ToList();

        FilteredItems.Clear();
        foreach (var item in filtered)
        {
            FilteredItems.Add(item);
        }

        TotalCount = _allItems.Count;
        PinnedCount = _allItems.Count(i => i.Pinned);
    }
}
