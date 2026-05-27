namespace Automated_FTP.Models;

/// <summary>
/// FTP_UPLOAD_CONFIG 表的实体。
/// 业务键：(CustCode, Device, Cp) 唯一确定一条配置。
/// </summary>
public class FtpUploadConfig
{
    public long Id { get; set; }

    public string CustCode { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;
    public string Cp { get; set; } = string.Empty;

    /// <summary>源文件目录，可含 {custCode}/{device} 等占位符。</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>源文件名匹配规则（子串或通配符 *.csv），可含占位符。</summary>
    public string SourceKeyword { get; set; } = string.Empty;

    /// <summary>文件处理方法注册键，对应 IFileProcessor.Name。</summary>
    public string ProcessorName { get; set; } = "PassThrough";

    public string? ProcessorParam { get; set; }

    /// <summary>改名规则注册键，对应 IFileRenamer.Name。</summary>
    public string RenamerName { get; set; } = "Template";

    public string? RenamerParam { get; set; }

    /// <summary>FTP / FTPS / SFTP。</summary>
    public string FtpProtocol { get; set; } = "FTP";

    public string FtpHost { get; set; } = string.Empty;
    public int FtpPort { get; set; } = 21;
    public string FtpUser { get; set; } = string.Empty;

    /// <summary>密文存储；运行时由 PasswordProtector 解密后再用。</summary>
    public string FtpPassword { get; set; } = string.Empty;

    /// <summary>远端目标目录，可含占位符。</summary>
    public string FtpTargetPath { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;
    public string? Remark { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
