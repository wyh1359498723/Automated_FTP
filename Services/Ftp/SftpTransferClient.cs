using Renci.SshNet;

namespace Automated_FTP.Services.Ftp;

/// <summary>
/// 基于 SSH.NET 的 SFTP 客户端。
/// </summary>
public class SftpTransferClient : IFileTransferClient
{
    private readonly ILogger _logger;
    private SftpClient? _client;

    public SftpTransferClient(ILogger logger)
    {
        _logger = logger;
    }

    public string Protocol => "SFTP";

    public Task ConnectAsync(string host, int port, string user, string plainPassword, int timeoutSeconds, CancellationToken ct = default)
    {
        var info = new Renci.SshNet.ConnectionInfo(host, port, user, new PasswordAuthenticationMethod(user, plainPassword))
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
        _client = new SftpClient(info)
        {
            OperationTimeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
        _client.Connect();
        _logger.LogInformation("SFTP 连接成功 {Host}:{Port}", host, port);
        return Task.CompletedTask;
    }

    public Task EnsureDirectoryAsync(string remoteDirectory, CancellationToken ct = default)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(remoteDirectory)) return Task.CompletedTask;

        var normalized = remoteDirectory.Replace('\\', '/');
        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = normalized.StartsWith('/') ? "" : ".";

        foreach (var part in parts)
        {
            current = current.Length == 0 ? "/" + part : current + "/" + part;
            if (!_client!.Exists(current))
            {
                _client.CreateDirectory(current);
            }
        }
        return Task.CompletedTask;
    }

    public async Task UploadAsync(string localFile, string remoteFullPath, CancellationToken ct = default)
    {
        EnsureConnected();
        var dir = Path.GetDirectoryName(remoteFullPath.Replace('\\', '/'))?.Replace('\\', '/');
        if (!string.IsNullOrEmpty(dir))
        {
            await EnsureDirectoryAsync(dir, ct);
        }

        await using var fs = File.OpenRead(localFile);
        _client!.UploadFile(fs, remoteFullPath.Replace('\\', '/'), canOverride: true);
    }

    public Task<bool> TestTargetReachableAsync(string remoteDirectory, CancellationToken ct = default)
    {
        EnsureConnected();
        if (string.IsNullOrWhiteSpace(remoteDirectory)) return Task.FromResult(true);

        var normalized = remoteDirectory.Replace('\\', '/');
        return Task.FromResult(_client!.Exists(normalized));
    }

    public Task DisconnectAsync()
    {
        if (_client is { IsConnected: true })
        {
            try { _client.Disconnect(); }
            catch (Exception ex) { _logger.LogWarning(ex, "SFTP 断开连接异常"); }
        }
        return Task.CompletedTask;
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
            throw new InvalidOperationException("SFTP 客户端尚未连接，请先调用 ConnectAsync。");
        }
    }
}
