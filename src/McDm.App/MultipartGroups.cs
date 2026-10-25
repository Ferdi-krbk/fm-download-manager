using System.Globalization;
using System.Windows.Data;
using McDm.Core;

namespace McDm.App;

public sealed class MultipartGroup
{
    public bool IsMultipart { get; init; }
    public DownloadItem Summary { get; } = new();
}

/// <summary>Presentation-only groups; downloads and persisted jobs remain independent.</summary>
public sealed class MultipartGroups : IValueConverter
{
    private readonly MultipartGroup _ungrouped = new();
    private readonly Dictionary<string, MultipartGroup> _groups = new(StringComparer.Ordinal);
    private HashSet<string> _numberedKeys = new(StringComparer.Ordinal);
    private Dictionary<string, string?> _membership = new();

    private MultipartIdentity? Identity(DownloadItem item)
    {
        var numbered = MultipartFiles.Describe(item.FileName, item.Directory);
        if (numbered != null) return numbered;
        var archive = MultipartFiles.BaseArchive(item.FileName, item.Directory);
        return archive != null && _numberedKeys.Contains(archive.Key) ? archive : null;
    }

    private MultipartGroup Group(MultipartIdentity identity)
    {
        if (!_groups.TryGetValue(identity.Key, out var group))
            _groups.Add(identity.Key, group = new MultipartGroup { IsMultipart = true });
        group.Summary.FileName = identity.Label;
        return group;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is DownloadItem item && Identity(item) is { } identity ? Group(identity) : _ungrouped;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

    public bool Refresh(IEnumerable<DownloadItem> visibleItems)
    {
        var items = visibleItems.ToList();
        _numberedKeys = items.Select(item => MultipartFiles.Describe(item.FileName, item.Directory)?.Key)
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
        var membership = new Dictionary<string, string?>();
        var batches = new Dictionary<string, List<DownloadItem>>();
        foreach (var item in items)
        {
            var identity = Identity(item);
            membership[item.Id] = identity?.Key;
            if (identity == null) continue;
            Group(identity);
            if (!batches.TryGetValue(identity.Key, out var batch)) batches.Add(identity.Key, batch = new());
            batch.Add(item);
        }
        // Observable additions/removals are already grouped by the view. Regroup only
        // an existing item whose actual filename/folder changed (or gained a base archive sibling).
        bool changed = membership.Any(pair => _membership.TryGetValue(pair.Key, out var old) && old != pair.Value);
        _membership = membership;
        bool turkish = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "tr";
        foreach (var (key, batch) in batches)
        {
            var summary = _groups[key].Summary;
            int completed = batch.Count(item => item.Status == DownloadStatus.Completed);
            summary.TotalBytes = batch.All(item => item.TotalBytes > 0) ? batch.Sum(item => item.TotalBytes) : -1;
            summary.DownloadedBytes = batch.Sum(item => item.DownloadedBytes);
            summary.IsTotalEstimated = batch.Any(item => item.IsTotalEstimated);
            summary.Speed = batch.Where(item => item.Status == DownloadStatus.Downloading).Sum(item => item.Speed);
            summary.Status = completed == batch.Count ? DownloadStatus.Completed
                : batch.Any(item => item.Status == DownloadStatus.Downloading) ? DownloadStatus.Downloading
                : batch.Any(item => item.Status == DownloadStatus.Queued) ? DownloadStatus.Queued
                : batch.Any(item => item.Status == DownloadStatus.Failed) ? DownloadStatus.Failed
                : batch.Any(item => item.Status == DownloadStatus.Paused) ? DownloadStatus.Paused : DownloadStatus.Cancelled;
            summary.Stage = turkish ? $"{batch.Count} parça · {completed} tamamlandı" : $"{batch.Count} parts · {completed} completed";
        }
        return changed;
    }
}
