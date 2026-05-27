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
/// 整个上传请求的汇总结果。
/// </summary>
public class UploadResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? RenderedSourcePath { get; set; }
    public string? RenderedTargetPath { get; set; }
    public List<UploadFileResult> Files { get; set; } = new();
}

/// <summary>
/// 文件检查结果（仅枚举不上传）。
/// </summary>
public class FileCheckResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? RenderedSourcePath { get; set; }
    public string? Keyword { get; set; }
    public List<string> MatchedFiles { get; set; } = new();
}

/// <summary>
/// FTP 连接检查结果。
/// </summary>
public class FtpCheckResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Protocol { get; set; }
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? RenderedTargetPath { get; set; }
    public bool TargetPathReachable { get; set; }
    public long DurationMs { get; set; }
}
