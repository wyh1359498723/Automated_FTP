namespace Automated_FTP.Services.Ftp;

public class FileTransferClientFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public FileTransferClientFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }

    public IFileTransferClient Create(string protocol)
    {
        return (protocol ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "FTP" => new FtpTransferClient(_loggerFactory.CreateLogger<FtpTransferClient>(), useTls: false),
            "FTPS" => new FtpTransferClient(_loggerFactory.CreateLogger<FtpTransferClient>(), useTls: true),
            "SFTP" => new SftpTransferClient(_loggerFactory.CreateLogger<SftpTransferClient>()),
            _ => throw new InvalidOperationException($"不支持的协议：'{protocol}'。仅支持 FTP / FTPS / SFTP。"),
        };
    }
}
