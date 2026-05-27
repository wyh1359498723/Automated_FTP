namespace Automated_FTP.Services.Renamers;

/// <summary>
/// 保留源文件的主文件名，扩展名跟随处理后文件的扩展名（由变量 {processedExt} 提供）。
/// 若 {processedExt} 不存在则沿用源文件的原始扩展名。
/// 例如：源文件 E0BF63_CP1_20260202141700.xls 经 ZipCompress 处理后，
///        得到 E0BF63_CP1_20260202141700.zip。
/// </summary>
public class KeepOriginalRenamer : IFileRenamer
{
    public string Name => "KeepOriginal";

    public string Rename(string originalFilePath, string? renamerParam, IReadOnlyDictionary<string, string?> variables)
    {
        var nameNoExt = Path.GetFileNameWithoutExtension(originalFilePath);
        var ext = variables.TryGetValue("processedExt", out var processedExt) && !string.IsNullOrEmpty(processedExt)
            ? processedExt
            : Path.GetExtension(originalFilePath);
        return nameNoExt + ext;
    }
}
