namespace Automated_FTP.Infrastructure.Email;

public class EmailAlertOptions
{
    public bool Enabled { get; set; }

    public string SmtpHost { get; set; } = string.Empty;

    public int SmtpPort { get; set; } = 25;

    public bool EnableSsl { get; set; }

    public string? UserName { get; set; }

    public string? Password { get; set; }

    public string From { get; set; } = string.Empty;

    public string[] To { get; set; } = Array.Empty<string>();

    public string SubjectPrefix { get; set; } = "[Automated_FTP]";
}
