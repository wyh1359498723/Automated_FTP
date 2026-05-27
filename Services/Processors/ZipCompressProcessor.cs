using System.IO.Compression;

namespace Automated_FTP.Services.Processors;

/// <summary>
/// 把源文件压缩成 ZIP 后上传。压缩包是临时文件，上传完成后由编排层自动清理。
///
/// PROCESSOR_PARAM（可选，JSON 格式）支持以下键：
///   zipEntryName  : ZIP 包内的文件名，默认与源文件名相同，支持占位符
///   compressionLevel : Optimal（默认）| Fastest | NoCompression | SmallestSize
///
/// 示例（不配置 PROCESSOR_PARAM，用所有默认值）：
///   PROCESSOR_NAME  = ZipCompress
///   PROCESSOR_PARAM = （留空）
///
/// 示例（自定义压缩级别 + 包内文件名）：
///   PROCESSOR_PARAM = {"compressionLevel":"Fastest","zipEntryName":"{wflot}_{originalName}"}
/// </summary>
public class ZipCompressProcessor : IFileProcessor
{
    private readonly ILogger<ZipCompressProcessor> _logger;
    private readonly PathTemplateRenderer _renderer;

    public ZipCompressProcessor(ILogger<ZipCompressProcessor> logger, PathTemplateRenderer renderer)
    {
        _logger = logger;
        _renderer = renderer;
    }

    public string Name => "ZipCompress";

    public async Task<ProcessResult> ProcessAsync(
        string sourceFile,
        string? processorParam,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken ct = default)
    {
        var options = ParseOptions(processorParam);
        var compressionLevel = ParseCompressionLevel(options.CompressionLevel);

        var originalName = Path.GetFileName(sourceFile);

        // ZIP 包内的文件名（可用占位符，默认与原文件名一致）
        var vars = new Dictionary<string, string?>(variables, StringComparer.OrdinalIgnoreCase)
        {
            ["originalName"] = originalName,
            ["originalNameNoExt"] = Path.GetFileNameWithoutExtension(sourceFile),
            ["ext"] = Path.GetExtension(sourceFile),
        };
        var entryName = string.IsNullOrWhiteSpace(options.ZipEntryName)
            ? originalName
            : _renderer.Render(options.ZipEntryName, vars, keepUnmatched: false);

        // 临时 ZIP 路径：和源文件同目录，避免跨盘符移动
        var tempZip = Path.Combine(
            Path.GetTempPath(),
            $"{Path.GetFileNameWithoutExtension(originalName)}_{Guid.NewGuid():N}.zip");

        _logger.LogInformation(
            "ZipCompress: 压缩 {Source} → {Zip}（包内名={Entry}，级别={Level}）",
            sourceFile, tempZip, entryName, compressionLevel);

        await Task.Run(() =>
        {
            using var zipStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: false);
            archive.CreateEntryFromFile(sourceFile, entryName, compressionLevel);
        }, ct);

        return new ProcessResult
        {
            ProcessedFile = tempZip,
            IsTemporary = true,   // 编排层上传完成后会自动删除此临时文件
        };
    }

    // ─────────────────────────── helpers ────────────────────────────────────

    private static ZipOptions ParseOptions(string? param)
    {
        if (string.IsNullOrWhiteSpace(param)) return new ZipOptions();

        try
        {
            var opts = System.Text.Json.JsonSerializer.Deserialize<ZipOptions>(param,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return opts ?? new ZipOptions();
        }
        catch
        {
            return new ZipOptions();
        }
    }

    private static CompressionLevel ParseCompressionLevel(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "fastest" => CompressionLevel.Fastest,
            "nocompression" => CompressionLevel.NoCompression,
            "smallestsize" => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal,
        };

    private class ZipOptions
    {
        public string? ZipEntryName { get; set; }
        public string? CompressionLevel { get; set; }
    }
}
