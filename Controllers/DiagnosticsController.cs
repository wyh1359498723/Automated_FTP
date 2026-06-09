using Automated_FTP.Models;
using Automated_FTP.Services;
using Microsoft.AspNetCore.Mvc;

namespace Automated_FTP.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly IFtpUploadService _service;

    public DiagnosticsController(IFtpUploadService service)
    {
        _service = service;
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
}
