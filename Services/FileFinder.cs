namespace Automated_FTP.Services;

/// <summary>
/// 按关键词/通配符在指定目录下查找文件。
/// 关键词规则：
///   - 含 * 或 ?       -> 视为通配符（Directory.EnumerateFiles 直接传）
///   - 不含通配符      -> 视为子串包含匹配（不区分大小写）
/// </summary>
public class FileFinder
{
    private readonly ILogger<FileFinder> _logger;

    public FileFinder(ILogger<FileFinder> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<string> Find(string directory, string keyword, bool recursive = false)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("源文件目录不能为空。", nameof(directory));
        }
        if (!Directory.Exists(directory))
        {
            _logger.LogWarning("源目录不存在：{Directory}", directory);
            return Array.Empty<string>();
        }

        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var hasWildcard = keyword.Contains('*') || keyword.Contains('?');

        IEnumerable<string> matches;
        if (hasWildcard)
        {
            matches = Directory.EnumerateFiles(directory, keyword, option);
        }
        else
        {
            matches = Directory
                .EnumerateFiles(directory, "*", option)
                .Where(p => Path.GetFileName(p)
                    .Contains(keyword, StringComparison.OrdinalIgnoreCase));
        }

        var list = matches.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        _logger.LogInformation(
            "在 {Directory} 中按关键词 \"{Keyword}\" 查找到 {Count} 个文件",
            directory, keyword, list.Count);
        return list;
    }
}
