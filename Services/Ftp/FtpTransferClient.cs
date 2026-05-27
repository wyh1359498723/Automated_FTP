using FluentFTP;

namespace Automated_FTP.Services.Ftp;

/// <summary>
/// 基于 FluentFTP 的 FTP / FTPS 客户端。
/// </summary>
public class FtpTransferClient : IFileTransferClient
{
    private readonly bool _useTls;
    private readonly ILogger _logger;
    private AsyncFtpClient? _client;

    public FtpTransferClient(ILogger logger, bool useTls)
    {
        _logger = logger;
        _useTls = useTls;
    }

    public string Protocol => _useTls ? "FTPS" : "FTP";

    public async Task ConnectAsync(string host, int port, string user, string plainPassword, int timeoutSeconds, CancellationToken ct = default)
    {
        _client = new AsyncFtpClient(host, user, plainPassword, port);
        _client.Config.ConnectTimeout = timeoutSeconds * 1000;
        _client.Config.ReadTimeout = timeoutSeconds * 1000;
        _client.Config.DataConnectionConnectTimeout = timeoutSeconds * 1000;
        _client.Config.DataConnectionReadTimeout = timeoutSeconds * 1000;

        if (_useTls)
        {
            _client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
            _client.Config.ValidateAnyCertificate = true;
        }

        await _client.Connect(ct);
        _logger.LogInformation("FTP 连接成功 {Host}:{Port} (TLS={Tls})", host, port, _useTls);
    }

    public async Task EnsureDirectoryAsync(string remoteDirectory, CancellationToken ct = default)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(remoteDirectory)) return;

        if (!await _client!.DirectoryExists(remoteDirectory, ct))
        {
            await _client!.CreateDirectory(remoteDirectory, true, ct);
        }
    }

    public async Task UploadAsync(string localFile, string remoteFullPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var status = await _client!.UploadFile(
            localFile,
            remoteFullPath,
            FtpRemoteExists.Overwrite,
            createRemoteDir: true,
            FtpVerify.None,
            null,
            ct);

        if (status != FtpStatus.Success)
        {
            throw new InvalidOperationException($"FTP 上传失败：状态={status}, file={remoteFullPath}");
        }
    }

    public async Task<bool> TestTargetReachableAsync(string remoteDirectory, CancellationToken ct = default)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(remoteDirectory)) return true;

        if (await _client!.DirectoryExists(remoteDirectory, ct))
        {
            await _client!.SetWorkingDirectory(remoteDirectory, ct);
            return true;
        }
        return false;
    }

    public async Task DisconnectAsync()
    {
        if (_client is { IsConnected: true })
        {
            try { await _client.Disconnect(); }
            catch (Exception ex) { _logger.LogWarning(ex, "FTP 断开连接异常"); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _client?.Dispose();
        _client = null;
        GC.SuppressFinalize(this);
    }

    private void EnsureConnected()
    {
        if (_client is null || !_client.IsConnected)
        {
            throw new InvalidOperationException("FTP 客户端尚未连接，请先调用 ConnectAsync。");
        }
    }
}
