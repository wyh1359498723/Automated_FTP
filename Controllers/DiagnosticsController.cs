using Automated_FTP.Infrastructure.Security;
using Automated_FTP.Models;
using Automated_FTP.Services;
using Microsoft.AspNetCore.Mvc;

namespace Automated_FTP.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly IFtpUploadService _service;
    private readonly PasswordProtector _passwordProtector;

    public DiagnosticsController(IFtpUploadService service, PasswordProtector passwordProtector)
    {
        _service = service;
        _passwordProtector = passwordProtector;
    }

    /// <summary>
    /// 仅按配置渲染源路径并枚举匹配文件，不做处理也不上传。
    /// </summary>
    [HttpPost("file-check")]
    [ProducesResponseType(typeof(FileCheckResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<FileCheckResult>> FileCheck([FromBody] UploadRequest request, CancellationToken ct)
    {
        var result = await _service.CheckFilesAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// 仅做 FTP/SFTP 连接测试 + 目标目录可达性测试。
    /// </summary>
    [HttpPost("ftp-check")]
    [ProducesResponseType(typeof(FtpCheckResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<FtpCheckResult>> FtpCheck([FromBody] UploadRequest request, CancellationToken ct)
    {
        var result = await _service.CheckFtpAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// 把明文密码加密成可入库的密文（开发/运维内部使用）。
    /// </summary>
    [HttpPost("protect-password")]
    [ProducesResponseType(typeof(ProtectPasswordResponse), StatusCodes.Status200OK)]
    public ActionResult<ProtectPasswordResponse> ProtectPassword([FromBody] ProtectPasswordRequest request)
    {
        var cipher = _passwordProtector.Protect(request.PlainText ?? string.Empty);
        return Ok(new ProtectPasswordResponse { Cipher = cipher });
    }
}

public class ProtectPasswordRequest
{
    public string? PlainText { get; set; }
}

public class ProtectPasswordResponse
{
    public string Cipher { get; set; } = string.Empty;
}
