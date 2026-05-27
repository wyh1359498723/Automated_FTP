using Automated_FTP.Models;

namespace Automated_FTP.Services.Ftp;

/// <summary>
/// 远程文件传输客户端的统一抽象（FTP / FTPS / SFTP）。
/// 调用方传入"已解密"的明文密码。
/// </summary>
public interface IFileTransferClient : IAsyncDisposable
{
    string Protocol { get; }

    Task ConnectAsync(string host, int port, string user, string plainPassword, int timeoutSeconds, CancellationToken ct = default);

    Task EnsureDirectoryAsync(string remoteDirectory, CancellationToken ct = default);

    Task UploadAsync(string localFile, string remoteFullPath, CancellationToken ct = default);

    /// <summary>
    /// 测试到目标目录的可达性：连接成功并能进入/创建该目录。
    /// </summary>
    Task<bool> TestTargetReachableAsync(string remoteDirectory, CancellationToken ct = default);

    Task DisconnectAsync();
}
