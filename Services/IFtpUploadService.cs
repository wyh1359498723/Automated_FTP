using Automated_FTP.Models;

namespace Automated_FTP.Services;

public interface IFtpUploadService
{
    Task<UploadResult> UploadAsync(UploadRequest request, CancellationToken ct = default);

    Task<FileCheckResult> CheckFilesAsync(UploadRequest request, CancellationToken ct = default);

    Task<FtpCheckResult> CheckFtpAsync(UploadRequest request, CancellationToken ct = default);
}
