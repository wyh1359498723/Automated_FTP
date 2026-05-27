namespace Automated_FTP.Services.Processors;

/// <summary>
/// 直接通过：不做任何加工，原文件即为上传文件。
/// </summary>
public class PassThroughProcessor : IFileProcessor
{
    public string Name => "PassThrough";

    public Task<ProcessResult> ProcessAsync(
        string sourceFile,
        string? processorParam,
        IReadOnlyDictionary<string, string?> variables,
        CancellationToken ct = default)
    {
        return Task.FromResult(new ProcessResult
        {
            ProcessedFile = sourceFile,
            IsTemporary = false,
        });
    }
}
