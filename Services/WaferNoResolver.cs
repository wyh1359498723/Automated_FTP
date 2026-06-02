using Automated_FTP.Models;

namespace Automated_FTP.Services;

/// <summary>
/// 解析 API 片号组（逗号分隔），供查找文件时逐个注入 {wf_no}。
/// </summary>
public static class WaferNoResolver
{
    public const string WfNoKey = "wf_no";
    public const string WfNosKey = "wf_nos";

    /// <summary>
    /// 从 UploadRequest.WaferNos 或 variables.wf_nos 等字段解析片号列表。
    /// </summary>
    public static IReadOnlyList<string> ResolveList(UploadRequest request)
    {
        return ParseCommaSeparated(GetRawWaferNos(request));
    }

    public static string? GetRawWaferNos(UploadRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.WaferNos))
            return request.WaferNos.Trim();
        if (request.Variables is not null)
            return GetVariable(request.Variables, WfNosKey, "waferNos", "WaferNos", "WF_NOS");
        return null;
    }

    public static IReadOnlyList<string> ParseCommaSeparated(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Array.Empty<string>();

        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.Length == 0 || !seen.Add(part))
                continue;
            list.Add(part);
        }
        return list;
    }

    private static string? GetVariable(IReadOnlyDictionary<string, string?> vars, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (vars.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
                return v;
        }
        return null;
    }
}
