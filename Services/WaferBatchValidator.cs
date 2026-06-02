namespace Automated_FTP.Services;

/// <summary>
/// 片号组完整性：传 waferNos 时每个片号都必须至少匹配到一个文件。
/// </summary>
public static class WaferBatchValidator
{
    public static IReadOnlyList<string> GetMissingWaferNosFromMatches(
        IReadOnlyList<string> waferNos,
        IEnumerable<(string? WfNo, int FileCount)> waferScans)
    {
        if (waferNos.Count == 0)
            return Array.Empty<string>();

        var found = waferScans
            .Where(s => !string.IsNullOrWhiteSpace(s.WfNo) && s.FileCount > 0)
            .Select(s => s.WfNo!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return waferNos.Where(wf => !found.Contains(wf)).ToList();
    }
}
