namespace Automated_FTP.Infrastructure.Email;

public interface IAlertEmailService
{
    Task SendWaferBatchMissingFilesAlertAsync(WaferBatchAlertContext context, CancellationToken ct = default);
}
