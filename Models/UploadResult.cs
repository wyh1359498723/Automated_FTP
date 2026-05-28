namespace Automated_FTP.Models;

/// <summary>
/// 单个文件的上传结果。
/// </summary>
public class UploadFileResult
{
    public string SourceFile { get; set; } = string.Empty;
    public string ProcessedFile { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string TargetFileName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public long DurationMs { get; set; }
}

/// <summary>
/// 单条配置的上传执行结果。
/// </summary>
public class UploadConfigResult
{
    public long ConfigId { get; set; }
    public string? Remark { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? RenderedSourcePath { get; set; }
    public string? RenderedTargetPath { get; set; }
    public List<UploadFileResult> Files { get; set; } = new();
}

/// <summary>
/// 整个上传请求的汇总结果（可包含多条配置）。
/// </summary>
public class UploadResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int ConfigCount { get; set; }
    public List<UploadConfigResult> Configs { get; set; } = new();

    /// <summary>所有配置的文件结果扁平列表（便于兼容旧客户端）。</summary>
    public List<UploadFileResult> Files { get; set; } = new();

    /// <summary>首条配置的渲染路径（兼容旧字段）。</summary>
    public string? RenderedSourcePath { get; set; }
    public string? RenderedTargetPath { get; set; }
}

/// <summary>
/// 单条配置的文件检查结果。
/// </summary>
public class FileCheckConfigResult
{
    public long ConfigId { get; set; }
    public string? Remark { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? RenderedSourcePath { get; set; }
    public string? Keyword { get; set; }
    public List<string> MatchedFiles { get; set; } = new();
}

/// <summary>
/// 文件检查结果（仅枚举不上传，可包含多条配置）。
/// </summary>
public class FileCheckResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int ConfigCount { get; set; }
    public List<FileCheckConfigResult> Configs { get; set; } = new();

    public string? RenderedSourcePath { get; set; }
    public string? Keyword { get; set; }
    public List<string> MatchedFiles { get; set; } = new();
}

/// <summary>
/// 单条配置的 FTP 连接检查结果。
/// </summary>
public class FtpCheckConfigResult
{
    public long ConfigId { get; set; }
    public string? Remark { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Protocol { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? RenderedTargetPath { get; set; }
    public bool TargetPathReachable { get; set; }
    public long DurationMs { get; set; }
}

/// <summary>
/// FTP 连接检查结果（可包含多条配置）。
/// </summary>
public class FtpCheckResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int ConfigCount { get; set; }
    public List<FtpCheckConfigResult> Configs { get; set; } = new();

    public string? Protocol { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? RenderedTargetPath { get; set; }
    public bool TargetPathReachable { get; set; }
    public long DurationMs { get; set; }
}
