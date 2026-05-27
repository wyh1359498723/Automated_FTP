using System.Text.RegularExpressions;

namespace Automated_FTP.Services;

/// <summary>
/// 把模板里 "{key}" 形式的占位符替换为变量字典里的对应值。
/// - 大小写不敏感
/// - 未匹配占位符默认保留原样（也可选择置空），由 keepUnmatched 参数控制
/// - 自动注入系统时间相关键：yyyyMMdd / yyyy / MM / dd / HHmmss / HH / mm / ss
/// </summary>
public class PathTemplateRenderer
{
    private static readonly Regex Pattern = new(@"\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

    public string Render(string? template, IReadOnlyDictionary<string, string?> variables, bool keepUnmatched = true)
    {
        if (string.IsNullOrEmpty(template)) return template ?? string.Empty;

        var merged = MergeWithSystemTokens(variables);

        return Pattern.Replace(template, m =>
        {
            var key = m.Groups[1].Value;
            if (merged.TryGetValue(key, out var v) && v is not null)
            {
                return v;
            }
            return keepUnmatched ? m.Value : string.Empty;
        });
    }

    private static Dictionary<string, string?> MergeWithSystemTokens(IReadOnlyDictionary<string, string?> input)
    {
        var dict = new Dictionary<string, string?>(input, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.Now;
        TrySet(dict, "yyyyMMdd", now.ToString("yyyyMMdd"));
        TrySet(dict, "yyyy", now.ToString("yyyy"));
        TrySet(dict, "MM", now.ToString("MM"));
        TrySet(dict, "dd", now.ToString("dd"));
        TrySet(dict, "HHmmss", now.ToString("HHmmss"));
        TrySet(dict, "HH", now.ToString("HH"));
        TrySet(dict, "mm", now.ToString("mm"));
        TrySet(dict, "ss", now.ToString("ss"));
        return dict;
    }

    private static void TrySet(Dictionary<string, string?> dict, string key, string value)
    {
        if (!dict.ContainsKey(key)) dict[key] = value;
    }
}
