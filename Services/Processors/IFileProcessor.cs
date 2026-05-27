namespace Automated_FTP.Services.Processors;

/// <summary>
/// 文件处理策略：把"原始源文件"转换为"待上传文件"。
/// 实现类只需绑定一个唯一的 Name（在配置表的 PROCESSOR_NAME 中引用）。
/// </summary>
public interface IFileProcessor
{
    /// <summary>注册键，对应 FTP_UPLOAD_CONFIG.PROCESSOR_NAME。大小写不敏感比较。</summary>
    string Name { get; }

    /// <summary>
    /// 处理一个源文件，返回处理后用于上传的文件路径。
    /// 如果不需要处理（如 PassThrough），可以直接返回源文件路径本身。
    /// 如果生成了临时文件，应在 <see cref="ProcessResult.IsTemporary"/> 中标识。
    /// </summary>
    Task<ProcessResult> ProcessAsync(
        string sourceFile,
        string? processorParam,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken ct = default);
}

public class ProcessResult
{
    public string ProcessedFile { get; set; } = string.Empty;

    /// <summary>true 表示是处理过程产生的临时文件，编排层在上传完成后会清理。</summary>
    public bool IsTemporary { get; set; }
}
