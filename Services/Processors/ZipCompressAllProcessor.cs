using System.IO.Compression;

namespace Automated_FTP.Services.Processors;

/// <summary>
/// 把所有匹配文件打进同一个 ZIP，最终只上传一个文件。
///
/// PROCESSOR_PARAM（可选，JSON 格式）支持以下键：
///   zipEntryName     : ZIP 包内每个文件的命名模板，默认为原文件名，支持占位符
///                      额外可用：{originalName} {originalNameNoExt} {ext} {index}（从1起）
///   compressionLevel : Optimal（默认）| Fastest | NoCompression | SmallestSize
///
/// 改名规则（RENAMER_NAME / RENAMER_PARAM）作用于整个 ZIP 包的文件名。
/// 可用额外变量：{fileCount}（被压入的文件数量）
///
/// 示例：
///   PROCESSOR_NAME  = ZipCompressAll
///   PROCESSOR_PARAM = {"compressionLevel":"Fastest"}
///   RENAMER_NAME    = Template
///   RENAMER_PARAM   = {wflot}_{cp}_{yyyyMMdd}.zip
/// </summary>
public class ZipCompressAllProcessor : IBatchFileProcessor
{
    private readonly ILogger<ZipCompressAllProcessor> _logger;
    private readonly PathTemplateRenderer _renderer;

    public ZipCompressAllProcessor(ILogger<ZipCompressAllProcessor> logger, PathTemplateRenderer renderer)
    {
        _logger = logger;
        _renderer = renderer;
    }

    public string Name => "ZipCompressAll";

    /// <summary>
    /// 单文件调用时退化为单文件批量（保持接口兼容，实际仍走批量逻辑）。
    /// </summary>
    public Task<ProcessResult> ProcessAsync(
        string sourceFile,
        string? processorParam,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken ct = default)
        => ProcessBatchAsync([sourceFile], processorParam, variables, ct);

    public async Task<ProcessResult> ProcessBatchAsync(
        IReadOnlyList<string> sourceFiles,
        string? processorParam,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken ct = default)
    {
        var options = ParseOptions(processorParam);
        var compressionLevel = ParseCompressionLevel(options.CompressionLevel);

        var tempZip = Path.Combine(
            Path.GetTempPath(),
            $"batch_{Guid.NewGuid():N}.zip");

        _logger.LogInformation(
            "ZipCompressAll: 压缩 {Count} 个文件 → {Zip}（级别={Level}）",
            sourceFiles.Count, tempZip, compressionLevel);

        await Task.Run(() =>
        {
            using var zipStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);

            for (var i = 0; i < sourceFiles.Count; i++)
            {
                var src = sourceFiles[i];
                var originalName = Path.GetFileName(src);

                var entryVars = new Dictionary<string, string?>(variables, StringComparer.OrdinalIgnoreCase)
                {
                    ["originalName"] = originalName,
                    ["originalNameNoExt"] = Path.GetFileNameWithoutExtension(src),
                    ["ext"] = Path.GetExtension(src),
                    ["index"] = (i + 1).ToString(),
                };

                var entryName = string.IsNullOrWhiteSpace(options.ZipEntryName)
                    ? originalName
                    : _renderer.Render(options.ZipEntryName, entryVars, keepUnmatched: false);

                archive.CreateEntryFromFile(src, entryName, compressionLevel);
                _logger.LogDebug("  [{Index}/{Total}] {Entry} ← {Src}", i + 1, sourceFiles.Count, entryName, src);
            }
        }, ct);

        return new ProcessResult
        {
            ProcessedFile = tempZip,
            IsTemporary = true,
        };
    }

    // ─────────────────────────── helpers ────────────────────────────────────

    private static ZipAllOptions ParseOptions(string? param)
    {
        if (string.IsNullOrWhiteSpace(param)) return new ZipAllOptions();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<ZipAllOptions>(param,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? new ZipAllOptions();
        }
        catch { return new ZipAllOptions(); }
    }

    private static CompressionLevel ParseCompressionLevel(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "fastest"         => CompressionLevel.Fastest,
            "nocompression"   => CompressionLevel.NoCompression,
            "smallestsize"    => CompressionLevel.SmallestSize,
            _                 => CompressionLevel.Optimal,
        };

    private class ZipAllOptions
    {
        public string? ZipEntryName { get; set; }
        public string? CompressionLevel { get; set; }
    }
}
