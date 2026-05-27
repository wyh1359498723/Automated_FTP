namespace Automated_FTP.Services.Renamers;

/// <summary>
/// 改名策略：根据源文件和变量字典生成上传后的目标文件名（不含路径）。
/// </summary>
public interface IFileRenamer
{
    /// <summary>注册键，对应 FTP_UPLOAD_CONFIG.RENAMER_NAME。大小写不敏感比较。</summary>
    string Name { get; }

    string Rename(
        string originalFilePath,
        string? renamerParam,
        IReadOnlyDictionary<string, string?> variables);
}
