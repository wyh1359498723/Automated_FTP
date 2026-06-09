using System.Diagnostics;
using Automated_FTP.Infrastructure.Email;
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
    private sealed record WaferFileScan(
        string? WfNo,
        Dictionary<string, string?> Vars,
        string RenderedSourcePath,
        string RenderedKeyword,
        List<string> Files);

    private sealed record FileFindMatch(
        string FilePath,
        Dictionary<string, string?> Vars,
        string? WfNo,
        string RenderedSourcePath,
        string RenderedKeyword);

    private readonly IConfigRepository _repo;
    private readonly PathTemplateRenderer _renderer;
    private readonly FileFinder _finder;
    private readonly FileProcessorRegistry _processors;
    private readonly FileRenamerRegistry _renamers;
    private readonly FileTransferClientFactory _transferFactory;
    private readonly PasswordProtector _passwordProtector;
    private readonly IAlertEmailService _alertEmail;
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
        IAlertEmailService alertEmail,
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
        _alertEmail = alertEmail;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken ct = default)
    {
        var (configs, vars, waferNos) = await ResolveConfigsAndVarsAsync(request, ct);
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
            var ok = await UploadOneConfigAsync(request, config, vars, waferNos, cfgResult, ct);
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
        var (configs, vars, waferNos) = await ResolveConfigsAndVarsAsync(request, ct);
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

            try
            {
                var scans = ScanFilesByWafer(config, vars, waferNos);
                ApplyFileCheckSummary(cfgResult, scans, waferNos);
                if (waferNos.Count > 0)
                {
                    var missing = GetMissingWaferNos(waferNos, scans);
                    cfgResult.MissingWaferNos = missing.ToList();
                    cfgResult.WaferBatchComplete = missing.Count == 0;
                    if (missing.Count > 0)
                    {
                        cfgResult.Success = false;
                        cfgResult.Error =
                            $"片号组完整性检查失败：片号 [{string.Join(", ", missing)}] 未找到文件。";
                        allOk = false;
                    }
                    else
                    {
                        cfgResult.Success = true;
                    }
                }
                else if (!TryValidateFileScans(scans, out var scanError))
                {
                    cfgResult.Success = false;
                    cfgResult.Error = scanError;
                    allOk = false;
                }
                else
                {
                    cfgResult.Success = true;
                }
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
        var (configs, vars, waferNos) = await ResolveConfigsAndVarsAsync(request, ct);
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

            var ftpVars = BuildFtpCheckVars(vars, waferNos);
            var targetDir = _renderer.Render(config.FtpTargetPath, ftpVars, keepUnmatched: false);
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
        UploadRequest request,
        FtpUploadConfig config,
        Dictionary<string, string?> vars,
        IReadOnlyList<string> waferNos,
        UploadConfigResult cfgResult,
        CancellationToken ct)
    {
        List<WaferFileScan> scans;
        try
        {
            scans = ScanFilesByWafer(config, vars, waferNos);
        }
        catch (Exception ex)
        {
            cfgResult.Success = false;
            cfgResult.Error = $"扫描源目录失败：{ex.Message}";
            return false;
        }

        if (waferNos.Count > 0)
        {
            var missing = GetMissingWaferNos(waferNos, scans);
            if (missing.Count > 0)
            {
                cfgResult.Success = false;
                cfgResult.WaferBatchAborted = true;
                cfgResult.MissingWaferNos = missing.ToList();
                cfgResult.Error =
                    $"片号组完整性检查失败：片号 [{string.Join(", ", missing)}] 未找到文件，本批次已全部取消 FTP 上传。";

                await SendWaferBatchAlertAsync(request, config, waferNos, missing, scans, cfgResult, ct);
                return false;
            }
        }
        else if (!TryValidateFileScans(scans, out var scanError))
        {
            cfgResult.Success = false;
            cfgResult.Error = scanError;
            cfgResult.RenderedSourcePath = scans.FirstOrDefault()?.RenderedSourcePath;
            return false;
        }

        var matches = FlattenScansToMatches(scans);
        cfgResult.RenderedSourcePath = SummarizeRenderedPaths(matches, m => m.RenderedSourcePath);
        cfgResult.RenderedTargetPath = _renderer.Render(
            config.FtpTargetPath,
            BuildFtpCheckVars(vars, waferNos),
            keepUnmatched: false);

        if (matches.Count == 0)
        {
            cfgResult.Success = false;
            cfgResult.Error = waferNos.Count > 0
                ? $"源目录中未匹配到文件（片号组：{string.Join(",", waferNos)}）。"
                : "源目录中没有匹配到文件。";
            return false;
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
            var targetDirs = matches
                .Select(m => _renderer.Render(config.FtpTargetPath, m.Vars, keepUnmatched: false))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var dir in targetDirs)
                await client.EnsureDirectoryAsync(dir, ct);
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
                batchProcessor, matches, config, renamer, client, cfgResult, ct);
        }
        else
        {
            allOk = await UploadPerFileAsync(
                processor, matches, config, renamer, client, cfgResult, ct);
        }

        cfgResult.Success = allOk;
        if (!allOk)
            cfgResult.Error = "存在上传失败的文件，详情见 Files。";
        return allOk;
    }

    /// <summary>逐文件处理并上传（PassThrough / ZipCompress 等单文件处理器）。</summary>
    private async Task<bool> UploadPerFileAsync(
        IFileProcessor processor,
        IReadOnlyList<FileFindMatch> matches,
        FtpUploadConfig config,
        IFileRenamer renamer,
        IFileTransferClient client,
        UploadConfigResult cfgResult,
        CancellationToken ct)
    {
        var allOk = true;
        foreach (var match in matches)
        {
            var src = match.FilePath;
            var vars = match.Vars;
            var perFile = new UploadFileResult { SourceFile = src, WfNo = match.WfNo };
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
                var targetDir = _renderer.Render(config.FtpTargetPath, vars, keepUnmatched: false);
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
                _logger.LogError(ex, "配置 ID={ConfigId} 上传文件失败 {File} wf_no={WfNo}", config.Id, src, match.WfNo);
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
        IReadOnlyList<FileFindMatch> matches,
        FtpUploadConfig config,
        IFileRenamer renamer,
        IFileTransferClient client,
        UploadConfigResult cfgResult,
        CancellationToken ct)
    {
        var files = matches.Select(m => m.FilePath).ToList();
        var batchVars = BuildBatchVars(matches);
        var perFile = new UploadFileResult
        {
            SourceFile = string.Join("\n", files),
            WfNo = matches.Count == 1 ? matches[0].WfNo : null,
        };
        var sw = Stopwatch.StartNew();
        ProcessResult? processed = null;
        try
        {
            processed = await processor.ProcessBatchAsync(files, config.ProcessorParam, batchVars, ct);
            perFile.ProcessedFile = processed.ProcessedFile;

            var renameVars = new Dictionary<string, string?>(batchVars, StringComparer.OrdinalIgnoreCase)
            {
                ["processedExt"] = Path.GetExtension(processed.ProcessedFile),
                ["fileCount"] = files.Count.ToString(),
            };
            var targetName = renamer.Rename(files[0], config.RenamerParam, renameVars);
            var targetDir = _renderer.Render(config.FtpTargetPath, batchVars, keepUnmatched: false);
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

    private async Task<(IReadOnlyList<FtpUploadConfig> configs, Dictionary<string, string?> vars, IReadOnlyList<string> waferNos)> ResolveConfigsAndVarsAsync(
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

        var waferNos = WaferNoResolver.ResolveList(request);
        if (waferNos.Count > 0)
            vars[WaferNoResolver.WfNosKey] = WaferNoResolver.JoinNormalized(waferNos);

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

        return (configs, vars, waferNos);
    }

    private List<WaferFileScan> ScanFilesByWafer(
        FtpUploadConfig config,
        Dictionary<string, string?> baseVars,
        IReadOnlyList<string> waferNos)
    {
        var scans = new List<WaferFileScan>();

        if (waferNos.Count == 0)
        {
            var vars = new Dictionary<string, string?>(baseVars, StringComparer.OrdinalIgnoreCase);
            var sourceDir = _renderer.Render(config.SourcePath, vars, keepUnmatched: false);
            var keyword = _renderer.Render(config.SourceKeyword, vars, keepUnmatched: false);
            scans.Add(new WaferFileScan(null, vars, sourceDir, keyword, _finder.Find(sourceDir, keyword).ToList()));
            return scans;
        }

        foreach (var wfNo in waferNos)
        {
            var vars = new Dictionary<string, string?>(baseVars, StringComparer.OrdinalIgnoreCase)
            {
                [WaferNoResolver.WfNoKey] = wfNo,
            };
            var sourceDir = _renderer.Render(config.SourcePath, vars, keepUnmatched: false);
            var keyword = _renderer.Render(config.SourceKeyword, vars, keepUnmatched: false);
            scans.Add(new WaferFileScan(wfNo, vars, sourceDir, keyword, _finder.Find(sourceDir, keyword).ToList()));
        }

        return scans;
    }

    private static List<FileFindMatch> FlattenScansToMatches(IReadOnlyList<WaferFileScan> scans)
    {
        var results = new List<FileFindMatch>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scan in scans)
        {
            foreach (var file in scan.Files)
            {
                if (!seen.Add(file))
                    continue;
                results.Add(new FileFindMatch(file, scan.Vars, scan.WfNo, scan.RenderedSourcePath, scan.RenderedKeyword));
            }
        }
        return results;
    }

    private static IReadOnlyList<string> GetMissingWaferNos(
        IReadOnlyList<string> waferNos,
        IReadOnlyList<WaferFileScan> scans) =>
        WaferBatchValidator.GetMissingWaferNosFromMatches(
            waferNos,
            scans.Select(s => (s.WfNo, s.Files.Count)));

    /// <summary>
    /// 未传片号组时：源目录必须存在且至少匹配到一个文件。
    /// </summary>
    private static bool TryValidateFileScans(IReadOnlyList<WaferFileScan> scans, out string? error)
    {
        error = null;
        if (scans.Count == 0)
        {
            error = "未执行文件扫描。";
            return false;
        }

        foreach (var scan in scans)
        {
            var dir = scan.RenderedSourcePath;
            if (string.IsNullOrWhiteSpace(dir))
            {
                error = "渲染后的源目录为空，请检查 SOURCE_PATH 模板与 variables 占位符。";
                return false;
            }

            if (!Directory.Exists(dir))
            {
                error = $"源目录不存在：{dir}";
                return false;
            }

            if (scan.Files.Count == 0)
            {
                error = $"源目录中未匹配到文件：{dir}，关键词：{scan.RenderedKeyword}";
                return false;
            }
        }

        return true;
    }

    private async Task SendWaferBatchAlertAsync(
        UploadRequest request,
        FtpUploadConfig config,
        IReadOnlyList<string> waferNos,
        IReadOnlyList<string> missing,
        IReadOnlyList<WaferFileScan> scans,
        UploadConfigResult cfgResult,
        CancellationToken ct)
    {
        var context = new WaferBatchAlertContext
        {
            CustCode = request.CustCode,
            Device = request.Device,
            Cp = request.Cp,
            ConfigId = config.Id,
            ConfigRemark = config.Remark,
            WaferNos = WaferNoResolver.GetRawWaferNos(request) ?? string.Join(",", waferNos),
            MissingWaferNos = missing,
            WaferDetails = scans
                .Where(s => s.WfNo is not null)
                .Select(s => new WaferScanDetail
                {
                    WfNo = s.WfNo!,
                    RenderedSourcePath = s.RenderedSourcePath,
                    Keyword = s.RenderedKeyword,
                    MatchedFileCount = s.Files.Count,
                })
                .ToList(),
        };

        await _alertEmail.SendWaferBatchMissingFilesAlertAsync(context, ct);
        cfgResult.EmailAlertSent = true;
    }

    private static void ApplyFileCheckSummary(
        FileCheckConfigResult cfgResult,
        IReadOnlyList<WaferFileScan> scans,
        IReadOnlyList<string> waferNos)
    {
        var matches = FlattenScansToMatches(scans);
        cfgResult.MatchedFiles = matches.Select(m => m.FilePath).ToList();
        cfgResult.RenderedSourcePath = SummarizeRenderedPaths(matches, m => m.RenderedSourcePath);
        cfgResult.Keyword = SummarizeRenderedPaths(matches, m => m.RenderedKeyword);

        if (waferNos.Count == 0)
            return;

        foreach (var scan in scans.Where(s => s.WfNo is not null))
        {
            cfgResult.WaferResults.Add(new WaferFileCheckResult
            {
                WfNo = scan.WfNo,
                RenderedSourcePath = scan.RenderedSourcePath,
                Keyword = scan.RenderedKeyword,
                MatchedFiles = scan.Files.ToList(),
            });
        }

        var missing = GetMissingWaferNos(waferNos, scans);
        cfgResult.MissingWaferNos = missing.ToList();
        cfgResult.WaferBatchComplete = missing.Count == 0;

        foreach (var wfNo in missing)
        {
            if (cfgResult.WaferResults.Any(r => string.Equals(r.WfNo, wfNo, StringComparison.OrdinalIgnoreCase)))
                continue;
            var scan = scans.FirstOrDefault(s => string.Equals(s.WfNo, wfNo, StringComparison.OrdinalIgnoreCase));
            cfgResult.WaferResults.Add(new WaferFileCheckResult
            {
                WfNo = wfNo,
                RenderedSourcePath = scan?.RenderedSourcePath,
                Keyword = scan?.RenderedKeyword,
            });
        }
    }

    private static Dictionary<string, string?> BuildFtpCheckVars(
        Dictionary<string, string?> baseVars,
        IReadOnlyList<string> waferNos)
    {
        if (waferNos.Count == 0)
            return new Dictionary<string, string?>(baseVars, StringComparer.OrdinalIgnoreCase);

        var vars = new Dictionary<string, string?>(baseVars, StringComparer.OrdinalIgnoreCase)
        {
            [WaferNoResolver.WfNoKey] = waferNos[0],
        };
        return vars;
    }

    private static Dictionary<string, string?> BuildBatchVars(IReadOnlyList<FileFindMatch> matches)
    {
        var baseVars = matches.Count > 0
            ? new Dictionary<string, string?>(matches[0].Vars, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var wfNos = matches
            .Select(m => m.WfNo)
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (wfNos.Count == 1)
            baseVars[WaferNoResolver.WfNoKey] = wfNos[0];
        return baseVars;
    }

    private static string SummarizeRenderedPaths<T>(
        IReadOnlyList<T> matches,
        Func<T, string> selector)
    {
        if (matches.Count == 0)
            return string.Empty;
        return string.Join(" | ", matches.Select(selector).Distinct(StringComparer.OrdinalIgnoreCase));
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
