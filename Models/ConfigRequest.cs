using System.ComponentModel.DataAnnotations;

namespace Automated_FTP.Models;

/// <summary>
/// 新增 / 修改配置行时的请求体。
/// ID 由 Oracle IDENTITY 自动生成，CreatedAt / UpdatedAt 由数据库默认值维护，均不需要传。
/// FtpPassword 传明文，接口层会自动调用 PasswordProtector 加密后再入库。
/// </summary>
public class ConfigRequest
{
    [Required, MaxLength(50)]
    public string CustCode { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Device { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Cp { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string SourcePath { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string SourceKeyword { get; set; } = string.Empty;

    [MaxLength(50)]
    public string ProcessorName { get; set; } = "PassThrough";

    [MaxLength(500)]
    public string? ProcessorParam { get; set; }

    [MaxLength(50)]
    public string RenamerName { get; set; } = "Template";

    [MaxLength(500)]
    public string? RenamerParam { get; set; }

    /// <summary>FTP / FTPS / SFTP</summary>
    [Required, MaxLength(10)]
    public string FtpProtocol { get; set; } = "FTP";

    [Required, MaxLength(200)]
    public string FtpHost { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int FtpPort { get; set; } = 21;

    [Required, MaxLength(100)]
    public string FtpUser { get; set; } = string.Empty;

    /// <summary>明文密码，接口会自动加密后存库。更新时若为空则保留原密码。</summary>
    [MaxLength(500)]
    public string? FtpPassword { get; set; }

    [Required, MaxLength(500)]
    public string FtpTargetPath { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    [MaxLength(500)]
    public string? Remark { get; set; }
}
