using System.ComponentModel.DataAnnotations;

namespace Automated_FTP.Models;

/// <summary>
/// API 入参。
/// 业务键 (CustCode, Device, Cp) 用于匹配配置表，
/// Variables 字典中的其余键（如 wflot）用于在路径/改名模板中替换占位符。
/// </summary>
public class UploadRequest
{
    [Required]
    public string CustCode { get; set; } = string.Empty;

    [Required]
    public string Device { get; set; } = string.Empty;

    [Required]
    public string Cp { get; set; } = string.Empty;

    /// <summary>
    /// 占位符变量字典，例如 { "wflot": "WL202605270001" }。
    /// 业务键也会自动放进去，无需重复传。
    /// </summary>
    public Dictionary<string, string?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 可选：指定要执行的配置 ID 列表。不传、null 或空数组表示执行该业务键下所有启用配置。
    /// 指定时须属于 (CustCode, Device, Cp) 且已启用，否则返回 400。
    /// </summary>
    public List<long>? ConfigIds { get; set; }
}
