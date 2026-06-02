using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.Extensions.Options;

namespace Automated_FTP.Infrastructure.Email;

public class SmtpAlertEmailService : IAlertEmailService
{
    private readonly EmailAlertOptions _options;
    private readonly ILogger<SmtpAlertEmailService> _logger;

    public SmtpAlertEmailService(IOptions<EmailAlertOptions> options, ILogger<SmtpAlertEmailService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendWaferBatchMissingFilesAlertAsync(WaferBatchAlertContext context, CancellationToken ct = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning(
                "片号组缺文件报警未发送（EmailAlert:Enabled=false）ConfigId={ConfigId} Missing=[{Missing}]",
                context.ConfigId, string.Join(",", context.MissingWaferNos));
            return;
        }

        var recipients = _options.To.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (recipients.Count == 0 || string.IsNullOrWhiteSpace(_options.From) || string.IsNullOrWhiteSpace(_options.SmtpHost))
        {
            _logger.LogError("邮件报警配置不完整（From/SmtpHost/To），无法发送片号缺文件报警。");
            return;
        }

        var subject = $"{_options.SubjectPrefix} 片号组缺文件，FTP 上传已取消";
        var body = BuildBody(context);

        using var message = new MailMessage
        {
            From = new MailAddress(_options.From),
            Subject = subject,
            Body = body,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
            IsBodyHtml = false,
        };
        foreach (var to in recipients)
            message.To.Add(to);

        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort)
        {
            EnableSsl = _options.EnableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
        };
        if (!string.IsNullOrWhiteSpace(_options.UserName))
            client.Credentials = new NetworkCredential(_options.UserName, _options.Password ?? string.Empty);
        else if (!string.IsNullOrWhiteSpace(_options.Password))
            client.Credentials = new NetworkCredential(_options.From, _options.Password);

        try
        {
            await client.SendMailAsync(message, ct);
            _logger.LogInformation(
                "已发送片号组缺文件报警邮件 ConfigId={ConfigId} To={To}",
                context.ConfigId, string.Join(";", recipients));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送片号组缺文件报警邮件失败 ConfigId={ConfigId}", context.ConfigId);
        }
    }

    private static string BuildBody(WaferBatchAlertContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Automated FTP 片号组完整性检查失败，本批次已全部取消 FTP 上传。");
        sb.AppendLine();
        sb.AppendLine($"业务键：{ctx.CustCode} / {ctx.Device} / {ctx.Cp}");
        sb.AppendLine($"配置 ID：{ctx.ConfigId}");
        if (!string.IsNullOrWhiteSpace(ctx.ConfigRemark))
            sb.AppendLine($"配置备注：{ctx.ConfigRemark}");
        sb.AppendLine($"请求片号组：{ctx.WaferNos}");
        sb.AppendLine($"缺失文件的片号：{string.Join(", ", ctx.MissingWaferNos)}");
        sb.AppendLine();
        sb.AppendLine("各片号扫描明细：");
        foreach (var d in ctx.WaferDetails)
        {
            sb.AppendLine($"  [wf_no={d.WfNo}] 匹配数={d.MatchedFileCount}");
            sb.AppendLine($"    源路径：{d.RenderedSourcePath}");
            sb.AppendLine($"    关键词：{d.Keyword}");
        }
        sb.AppendLine();
        sb.AppendLine($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        return sb.ToString();
    }
}
