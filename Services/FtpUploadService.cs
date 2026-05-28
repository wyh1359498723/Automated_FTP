using System.Diagnostics;
using Automated_FTP.Infrastructure.Security;
using Automated_FTP.Models;
using Automated_FTP.Repositories;
using Automated_FTP.Services.Ftp;
using Automated_FTP.Services.Processors;
using Automated_FTP.Services.Renamers;
using Microsoft.AspNetCore.Http;
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
        var (configs, vars) = await ResolveConfigsAndVarsAsync(request, ct);
        var result = new UploadResult { ConfigCount = configs.Count };

        if (configs.Count == 0)
        {
            result.Success = false;
            result.Error = $"未找到启用的配置：custCode={request.CustCode}, device={request.Device}, cp={request.Cp}";
            return result;
        }

        var allOk = true;
        foreach (var config in configs)
        {
            var cfgResult = new UploadConfigResult
            {
                ConfigId = config.Id,
                Remark = config.Remark,
            };
            var ok = await UploadOneConfigAsync(config, vars, cfgResult, ct);
            result.Configs.Add(cfgResult);
            result.Files.AddRange(cfgResult.Files);
            if (!ok) allOk = false;
        }

        ApplyUploadCompatFields(result);
        result.Success = allOk;
        if (!allOk)
            result.Error = "部分配置执行失败，详见 Configs。";
        return result;
    }

    public async Task<FileCheckResult> CheckFilesAsync(UploadRequest request, CancellationToken ct = default)
    {
        var (configs, vars) = await ResolveConfigsAndVarsAsync(request, ct);
        var result = new FileCheckResult { ConfigCount = configs.Count };

        if (configs.Count == 0)
        {
            result.Success = false;
            result.Error = $"未找到启用的配置：custCode={request.CustCode}, device={request.Device}, cp={request.Cp}";
            return result;
        }

        var allOk = true;
        foreach (var config in configs)
        {
            var cfgResult = new FileCheckConfigResult
            {
                ConfigId = config.Id,
                Remark = config.Remark,
            };

            var sourceDir = _renderer.Render(config.SourcePath, vars, keepUnmatched: false);
            var keyword = _renderer.Render(config.SourceKeyword, vars, keepUnmatched: false);
            cfgResult.RenderedSourcePath = sourceDir;
            cfgResult.Keyword = keyword;

            try
            {
                cfgResult.MatchedFiles = _finder.Find(sourceDir, keyword).ToList();
                cfgResult.Success = true;
            }
            catch (Exception ex)
            {
                cfgResult.Success = false;
                cfgResult.Error = ex.Message;
                allOk = false;
            }

            result.Configs.Add(cfgResult);
            result.MatchedFiles.AddRange(cfgResult.MatchedFiles);
        }

        ApplyFileCheckCompatFields(result);
        result.Success = allOk;
        if (!allOk)
            result.Error = "部分配置检查失败，详见 Configs。";
        return result;
    }

    public async Task<FtpCheckResult> CheckFtpAsync(UploadRequest request, CancellationToken ct = default)
    {
        var (configs, vars) = await ResolveConfigsAndVarsAsync(request, ct);
        var result = new FtpCheckResult { ConfigCount = configs.Count };

        if (configs.Count == 0)
        {
            result.Success = false;
            result.Error = $"未找到启用的配置：custCode={request.CustCode}, device={request.Device}, cp={request.Cp}";
            return result;
        }

        var allOk = true;
        foreach (var config in configs)
        {
            var cfgResult = new FtpCheckConfigResult
            {
                ConfigId = config.Id,
                Remark = config.Remark,
            };

            var targetDir = _renderer.Render(config.FtpTargetPath, vars, keepUnmatched: false);
            cfgResult.Protocol = config.FtpProtocol;
            cfgResult.Host = config.FtpHost;
            cfgResult.Port = config.FtpPort;
            cfgResult.RenderedTargetPath = targetDir;

            var sw = Stopwatch.StartNew();
            try
            {
                var plainPwd = _passwordProtector.Unprotect(config.FtpPassword);
                await using var client = _transferFactory.Create(config.FtpProtocol);
                await client.ConnectAsync(config.FtpHost, config.FtpPort, config.FtpUser, plainPwd, _options.DefaultFtpTimeoutSeconds, ct);
                cfgResult.TargetPathReachable = await client.TestTargetReachableAsync(targetDir, ct);
                cfgResult.Success = true;
            }
            catch (Exception ex)
            {
                cfgResult.Success = false;
                cfgResult.Error = ex.Message;
                allOk = false;
            }
            finally
            {
                sw.Stop();
                cfgResult.DurationMs = sw.ElapsedMilliseconds;
            }

            result.Configs.Add(cfgResult);
        }

        ApplyFtpCheckCompatFields(result);
        result.Success = allOk;
        result.DurationMs = result.Configs.Sum(c => c.DurationMs);
        if (!allOk)
            result.Error = "部分配置 FTP 检查失败，详见 Configs。";
        return result;
    }

    private async Task<bool> UploadOneConfigAsync(
        FtpUploadConfig config,
        Dictionary<string, string?> vars,
        UploadConfigResult cfgResult,
        CancellationToken ct)
    {
        var sourceDir = _renderer.Render(config.SourcePath, vars, keepUnmatched: false);
        var keyword = _renderer.Render(config.SourceKeyword, vars, keepUnmatched: false);
        var targetDir = _renderer.Render(config.FtpTargetPath, vars, keepUnmatched: false);
        cfgResult.RenderedSourcePath = sourceDir;
        cfgResult.RenderedTargetPath = targetDir;

        IReadOnlyList<string> files;
        try { files = _finder.Find(sourceDir, keyword); }
        catch (Exception ex)
        {
            cfgResult.Success = false;
            cfgResult.Error = $"扫描源目录失败：{ex.Message}";
            return false;
        }

        if (files.Count == 0)
        {
            cfgResult.Success = true;
            cfgResult.Error = "源目录中没有匹配到文件。";
            return true;
        }

        var processor = _processors.Resolve(config.ProcessorName);
        var renamer = _renamers.Resolve(config.RenamerName);

        string plainPwd;
        try { plainPwd = _passwordProtector.Unprotect(config.FtpPassword); }
        catch (Exception ex)
        {
            cfgResult.Success = false;
            cfgResult.Error = ex.Message;
            return false;
        }

        await using var client = _transferFactory.Create(config.FtpProtocol);
        try
        {
            await client.ConnectAsync(config.FtpHost, config.FtpPort, config.FtpUser, plainPwd, _options.DefaultFtpTimeoutSeconds, ct);
            await client.EnsureDirectoryAsync(targetDir, ct);
        }
        catch (Exception ex)
        {
            cfgResult.Success = false;
            cfgResult.Error = $"连接 FTP 失败：{ex.Message}";
            return false;
        }

        bool allOk;
        if (processor is IBatchFileProcessor batchProcessor)
        {
            allOk = await UploadBatchAsync(
                batchProcessor, files, config, renamer, vars, targetDir, client, cfgResult, ct);
        }
        else
        {
            allOk = await UploadPerFileAsync(
                processor, files, config, renamer, vars, targetDir, client, cfgResult, ct);
        }

        cfgResult.Success = allOk;
        if (!allOk)
            cfgResult.Error = "存在上传失败的文件，详情见 Files。";
        return allOk;
    }

    /// <summary>逐文件处理并上传（PassThrough / ZipCompress 等单文件处理器）。</summary>
    private async Task<bool> UploadPerFileAsync(
        IFileProcessor processor,
        IReadOnlyList<string> files,
        FtpUploadConfig config,
        IFileRenamer renamer,
        Dictionary<string, string?> vars,
        string targetDir,
        IFileTransferClient client,
        UploadConfigResult cfgResult,
        CancellationToken ct)
    {
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
                _logger.LogError(ex, "配置 ID={ConfigId} 上传文件失败 {File}", config.Id, src);
            }
            finally
            {
                CleanTemp(processed);
                sw.Stop();
                perFile.DurationMs = sw.ElapsedMilliseconds;
                cfgResult.Files.Add(perFile);
            }
        }
        return allOk;
    }

    /// <summary>
    /// 批量处理：所有文件合并成一个输出（如 ZipCompressAll），只上传一次。
    /// UploadFileResult.SourceFile 列出所有源文件路径（换行分隔）。
    /// </summary>
    private async Task<bool> UploadBatchAsync(
        IBatchFileProcessor processor,
        IReadOnlyList<string> files,
        FtpUploadConfig config,
        IFileRenamer renamer,
        Dictionary<string, string?> vars,
        string targetDir,
        IFileTransferClient client,
        UploadConfigResult cfgResult,
        CancellationToken ct)
    {
        var perFile = new UploadFileResult
        {
            SourceFile = string.Join("\n", files),
        };
        var sw = Stopwatch.StartNew();
        ProcessResult? processed = null;
        try
        {
            processed = await processor.ProcessBatchAsync(files, config.ProcessorParam, vars, ct);
            perFile.ProcessedFile = processed.ProcessedFile;

            var renameVars = new Dictionary<string, string?>(vars, StringComparer.OrdinalIgnoreCase)
            {
                ["processedExt"] = Path.GetExtension(processed.ProcessedFile),
                ["fileCount"] = files.Count.ToString(),
            };
            var targetName = renamer.Rename(files[0], config.RenamerParam, renameVars);
            perFile.TargetFileName = targetName;
            perFile.TargetPath = JoinRemote(targetDir, targetName);

            await client.UploadAsync(processed.ProcessedFile, perFile.TargetPath, ct);
            perFile.Success = true;
        }
        catch (Exception ex)
        {
            perFile.Success = false;
            perFile.Error = ex.Message;
            _logger.LogError(ex, "配置 ID={ConfigId} 批量上传失败（{Count} 个文件）", config.Id, files.Count);
        }
        finally
        {
            CleanTemp(processed);
            sw.Stop();
            perFile.DurationMs = sw.ElapsedMilliseconds;
            cfgResult.Files.Add(perFile);
        }
        return perFile.Success;
    }

    private void CleanTemp(ProcessResult? processed)
    {
        if (processed is { IsTemporary: true } && File.Exists(processed.ProcessedFile))
        {
            try { File.Delete(processed.ProcessedFile); }
            catch (Exception ex) { _logger.LogWarning(ex, "清理临时文件失败 {File}", processed.ProcessedFile); }
        }
    }

    private async Task<(IReadOnlyList<FtpUploadConfig> configs, Dictionary<string, string?> vars)> ResolveConfigsAndVarsAsync(
        UploadRequest request, CancellationToken ct)
    {
        var vars = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["custCode"] = request.CustCode,
            ["device"] = request.Device,
            ["cp"] = request.Cp,
        };
        if (request.Variables is not null)
        {
            foreach (var kv in request.Variables)
                vars[kv.Key] = kv.Value;
        }

        IReadOnlyList<FtpUploadConfig> configs;
        if (request.ConfigIds is null || request.ConfigIds.Count == 0)
        {
            configs = await _repo.FindAllByKeyAsync(request.CustCode, request.Device, request.Cp, ct);
        }
        else
        {
            var requestedIds = request.ConfigIds.Distinct().ToList();
            configs = await _repo.FindByIdsAndKeyAsync(
                request.CustCode, request.Device, request.Cp, requestedIds, ct);
            await ValidateRequestedConfigIdsAsync(request, requestedIds, configs, ct);
        }

        return (configs, vars);
    }

    private async Task ValidateRequestedConfigIdsAsync(
        UploadRequest request,
        IReadOnlyList<long> requestedIds,
        IReadOnlyList<FtpUploadConfig> found,
        CancellationToken ct)
    {
        var foundIds = found.Select(c => c.Id).ToHashSet();
        var missing = requestedIds.Where(id => !foundIds.Contains(id)).ToList();
        if (missing.Count == 0)
            return;

        var errors = new List<string>();
        foreach (var id in missing)
        {
            var cfg = await _repo.GetByIdAsync(id, ct);
            if (cfg is null)
                errors.Add($"配置 ID={id} 不存在。");
            else if (!cfg.Enabled)
                errors.Add($"配置 ID={id} 已禁用。");
            else if (!KeysEqual(cfg, request))
                errors.Add($"配置 ID={id} 的业务键为 {cfg.CustCode}/{cfg.Device}/{cfg.Cp}，与请求 {request.CustCode}/{request.Device}/{request.Cp} 不一致。");
            else
                errors.Add($"配置 ID={id} 不可用。");
        }

        throw new BadHttpRequestException(string.Join(" ", errors));
    }

    private static bool KeysEqual(FtpUploadConfig cfg, UploadRequest request) =>
        string.Equals(cfg.CustCode, request.CustCode, StringComparison.OrdinalIgnoreCase)
        && string.Equals(cfg.Device, request.Device, StringComparison.OrdinalIgnoreCase)
        && string.Equals(cfg.Cp, request.Cp, StringComparison.OrdinalIgnoreCase);

    private static void ApplyUploadCompatFields(UploadResult result)
    {
        var first = result.Configs.FirstOrDefault();
        if (first is null) return;
        result.RenderedSourcePath = first.RenderedSourcePath;
        result.RenderedTargetPath = first.RenderedTargetPath;
    }

    private static void ApplyFileCheckCompatFields(FileCheckResult result)
    {
        var first = result.Configs.FirstOrDefault();
        if (first is null) return;
        result.RenderedSourcePath = first.RenderedSourcePath;
        result.Keyword = first.Keyword;
    }

    private static void ApplyFtpCheckCompatFields(FtpCheckResult result)
    {
        var first = result.Configs.FirstOrDefault();
        if (first is null) return;
        result.Protocol = first.Protocol;
        result.Host = first.Host;
        result.Port = first.Port;
        result.RenderedTargetPath = first.RenderedTargetPath;
        result.TargetPathReachable = first.TargetPathReachable;
    }

    private static string JoinRemote(string dir, string fileName)
    {
        if (string.IsNullOrEmpty(dir)) return fileName;
        var d = dir.Replace('\\', '/').TrimEnd('/');
        return d + "/" + fileName;
    }
}
