namespace Automated_FTP.Services.Processors;

/// <summary>
/// 批量文件处理策略：把多个源文件合并处理为一个输出文件（如打成一个 ZIP）。
/// 同时继承 IFileProcessor，使注册表可以统一管理；
/// 服务层通过 is IBatchFileProcessor 判断是否走批量分支。
/// </summary>
public interface IBatchFileProcessor : IFileProcessor
{
    /// <summary>
    /// 把 <paramref name="sourceFiles"/> 中所有文件合并处理，返回单个输出文件。
    /// 若输出是临时文件，应在 <see cref="ProcessResult.IsTemporary"/> 中标识。
    /// </summary>
    Task<ProcessResult> ProcessBatchAsync(
        IReadOnlyList<string> sourceFiles,
        string? processorParam,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken ct = default);
}
