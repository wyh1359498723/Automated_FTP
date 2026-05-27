namespace Automated_FTP.Services.Renamers;

/// <summary>
/// 模板改名：renamerParam 即为目标文件名模板，例如：
///     {custCode}_{device}_{wflot}_{yyyyMMdd}_{originalName}
/// 自动注入：originalName / originalNameNoExt / ext。
/// 若 renamerParam 为空，则保留原文件名。
/// </summary>
public class TemplateRenamer : IFileRenamer
{
    private readonly PathTemplateRenderer _renderer;

    public TemplateRenamer(PathTemplateRenderer renderer)
    {
        _renderer = renderer;
    }

    public string Name => "Template";

    public string Rename(string originalFilePath, string? renamerParam, IReadOnlyDictionary<string, string?> variables)
    {
        var originalName = Path.GetFileName(originalFilePath);
        if (string.IsNullOrWhiteSpace(renamerParam))
        {
            return originalName;
        }

        var enriched = new Dictionary<string, string?>(variables, StringComparer.OrdinalIgnoreCase)
        {
            ["originalName"] = originalName,
            ["originalNameNoExt"] = Path.GetFileNameWithoutExtension(originalFilePath),
            ["ext"] = Path.GetExtension(originalFilePath),
        };

        var rendered = _renderer.Render(renamerParam, enriched, keepUnmatched: false);

        return string.IsNullOrWhiteSpace(rendered) ? originalName : rendered;
    }
}
