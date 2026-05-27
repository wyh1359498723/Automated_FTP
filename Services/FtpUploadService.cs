using System.Diagnostics;
using Automated_FTP.Infrastructure.Security;
using Automated_FTP.Models;
using Automated_FTP.Repositories;
using Automated_FTP.Services.Ftp;
using Automated_FTP.Services.Processors;
using Automated_FTP.Services.Renamers;
using Microsoft.Extensions.Options;

namespace Automated_FTP.Services;

public class FtpUploadService : IFtpUploadService
{
    private readonly IConfigRepository _repo;
    private readonly PathTemplateRenderer _renderer;
    private readonly FileFinder _finder;
    private readonly FileProcessorRegistry _processors;
    private readonly FileRenamerRegistry _renamers;
    private readonly FileTransferClientFactory _transferFactory;
    private readonly PasswordProtector _passwordProtector;
    private readonly UploadOptions _options;
    private readonly ILogger<FtpUploadService> _logger;

    public FtpUploadService(
        IConfigRepository repo,
        PathTemplateRenderer renderer,
        FileFinder finder,
        FileProcessorRegistry processors,
        FileRenamerRegistry renamers,
        FileTransferClientFactory transferFactory,
        PasswordProtector passwordProtector,
        IOptions<UploadOptions> options,
        ILogger<FtpUploadService> logger)
    {
        _repo = repo;
        _renderer = renderer;
        _finder = finder;
        _processors = processors;
        _renamers = renamers;
        _transferFactory = transferFactory;
        _passwordProtector = passwordProtector;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken ct = default)
    {
        var (config, vars) = await ResolveConfigAndVarsAsync(request, ct);
        var result = new UploadResult();
        if (config is null)
        {
            result.Success = false;
            result.Error = $"未找到启用的配置：custCode={request.CustCode}, device={request.Device}, cp={request.Cp}";
            return result;
        }

        var sourceDir = _renderer.Render(config.SourcePath, vars, keepUnmatched: false);
        var keyword = _renderer.Render(config.SourceKeyword, vars, keepUnmatched: false);
        var targetDir = _renderer.Render(config.FtpTargetPath, vars, keepUnmatched: false);
        result.RenderedSourcePath = sourceDir;
        result.RenderedTargetPath = targetDir;

        IReadOnlyList<string> files;
        try { files = _finder.Find(sourceDir, keyword); }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = $"扫描源目录失败：{ex.Message}";
            return result;
        }

        if (files.Count == 0)
        {
            result.Success = true;
            result.Error = "源目录中没有匹配到文件。";
            return result;
        }

        var processor = _processors.Resolve(config.ProcessorName);
        var renamer = _renamers.Resolve(config.RenamerName);

        string plainPwd;
        try { plainPwd = _passwordProtector.Unprotect(config.FtpPassword); }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
            return result;
        }

        await using var client = _transferFactory.Create(config.FtpProtocol);
        try
        {
            await client.ConnectAsync(config.FtpHost, config.FtpPort, config.FtpUser, plainPwd, _options.DefaultFtpTimeoutSeconds, ct);
            await client.EnsureDirectoryAsync(targetDir, ct);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = $"连接 FTP 失败：{ex.Message}";
            return result;
        }

        var allOk = true;
        foreach (var src in files)
        {
            var perFile = new UploadFileResult { SourceFile = src };
            var sw = Stopwatch.StartNew();
            ProcessResult? processed = null;
            try
            {
                processed = await processor.ProcessAsync(src, config.ProcessorParam, vars, ct);
                perFile.ProcessedFile = processed.ProcessedFile;

                // 改名器始终基于源文件名计算目标名，但额外注入 {processedExt} 方便 ZIP 等场景
                var renameVars = new Dictionary<string, string?>(vars, StringComparer.OrdinalIgnoreCase)
                {
                    ["processedExt"] = Path.GetExtension(processed.ProcessedFile),
                };
                var targetName = renamer.Rename(src, config.RenamerParam, renameVars);
                perFile.TargetFileName = targetName;
                perFile.TargetPath = JoinRemote(targetDir, targetName);

                await client.UploadAsync(processed.ProcessedFile, perFile.TargetPath, ct);
                perFile.Success = true;
            }
            catch (Exception ex)
            {
                allOk = false;
                perFile.Success = false;
                perFile.Error = ex.Message;
                _logger.LogError(ex, "上传文件失败 {File}", src);
            }
            finally
            {
                if (processed is { IsTemporary: true } && File.Exists(processed.ProcessedFile))
                {
                    try { File.Delete(processed.ProcessedFile); }
                    catch (Exception ex) { _logger.LogWarning(ex, "清理临时文件失败 {File}", processed.ProcessedFile); }
                }
                sw.Stop();
                perFile.DurationMs = sw.ElapsedMilliseconds;
                result.Files.Add(perFile);
            }
        }

        result.Success = allOk;
        if (!allOk) result.Error = "存在上传失败的文件，详情见 Files。";
        return result;
    }

    public async Task<FileCheckResult> CheckFilesAsync(UploadRequest request, CancellationToken ct = default)
    {
        var (config, vars) = await ResolveConfigAndVarsAsync(request, ct);
        var result = new FileCheckResult();
        if (config is null)
        {
            result.Success = false;
            result.Error = $"未找到启用的配置：custCode={request.CustCode}, device={request.Device}, cp={request.Cp}";
            return result;
        }

        var sourceDir = _renderer.Render(config.SourcePath, vars, keepUnmatched: false);
        var keyword = _renderer.Render(config.SourceKeyword, vars, keepUnmatched: false);
        result.RenderedSourcePath = sourceDir;
        result.Keyword = keyword;

        try
        {
            var files = _finder.Find(sourceDir, keyword);
            result.MatchedFiles = files.ToList();
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }
        return result;
    }

    public async Task<FtpCheckResult> CheckFtpAsync(UploadRequest request, CancellationToken ct = default)
    {
        var (config, vars) = await ResolveConfigAndVarsAsync(request, ct);
        var result = new FtpCheckResult();
        if (config is null)
        {
            result.Success = false;
            result.Error = $"未找到启用的配置：custCode={request.CustCode}, device={request.Device}, cp={request.Cp}";
            return result;
        }

        var targetDir = _renderer.Render(config.FtpTargetPath, vars, keepUnmatched: false);
        result.Protocol = config.FtpProtocol;
        result.Host = config.FtpHost;
        result.Port = config.FtpPort;
        result.RenderedTargetPath = targetDir;

        var sw = Stopwatch.StartNew();
        try
        {
            var plainPwd = _passwordProtector.Unprotect(config.FtpPassword);
            await using var client = _transferFactory.Create(config.FtpProtocol);
            await client.ConnectAsync(config.FtpHost, config.FtpPort, config.FtpUser, plainPwd, _options.DefaultFtpTimeoutSeconds, ct);
            result.TargetPathReachable = await client.TestTargetReachableAsync(targetDir, ct);
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Error = ex.Message;
        }
        finally
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
        }
        return result;
    }

    /// <summary>
    /// 1) 按业务键查配置；
    /// 2) 把 (业务键 + 配置基本字段 + 请求 variables) 合并成最终变量字典。
    /// </summary>
    private async Task<(FtpUploadConfig? config, Dictionary<string, string?> vars)> ResolveConfigAndVarsAsync(
        UploadRequest request, CancellationToken ct)
    {
        var config = await _repo.FindByKeyAsync(request.CustCode, request.Device, request.Cp, ct);

        var vars = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["custCode"] = request.CustCode,
            ["device"] = request.Device,
            ["cp"] = request.Cp,
        };
        if (request.Variables is not null)
        {
            foreach (var kv in request.Variables)
            {
                vars[kv.Key] = kv.Value;
            }
        }

        return (config, vars);
    }

    private static string JoinRemote(string dir, string fileName)
    {
        if (string.IsNullOrEmpty(dir)) return fileName;
        var d = dir.Replace('\\', '/').TrimEnd('/');
        return d + "/" + fileName;
    }
}
