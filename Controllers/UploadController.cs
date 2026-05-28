using Automated_FTP.Models;
using Automated_FTP.Services;
using Microsoft.AspNetCore.Mvc;

namespace Automated_FTP.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UploadController : ControllerBase
{
    private readonly IFtpUploadService _service;

    public UploadController(IFtpUploadService service)
    {
        _service = service;
    }

    /// <summary>
    /// 主上传流程：按 (custCode,device,cp) 匹配配置；可选 configIds 指定子集，扫描源目录，处理 + 改名 + 上传。
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(UploadResult), StatusCodes.Status200OK)]
    public async Task<ActionResult<UploadResult>> Post([FromBody] UploadRequest request, CancellationToken ct)
    {
        var result = await _service.UploadAsync(request, ct);
        return Ok(result);
    }
}
