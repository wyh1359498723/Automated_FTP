using Automated_FTP.Models;

using Automated_FTP.Repositories;

using Microsoft.AspNetCore.Mvc;



namespace Automated_FTP.Controllers;



/// <summary>

/// 配置表 CRUD 管理接口。FtpPassword 明文存库并在查询时返回。

/// </summary>

[ApiController]

[Route("api/[controller]")]

public class ConfigController : ControllerBase

{

    private readonly IConfigRepository _repo;



    public ConfigController(IConfigRepository repo)

    {

        _repo = repo;

    }



    /// <summary>查询所有配置（含禁用行）。</summary>

    [HttpGet]

    [ProducesResponseType(typeof(IReadOnlyList<FtpUploadConfigDto>), StatusCodes.Status200OK)]

    public async Task<ActionResult<IReadOnlyList<FtpUploadConfigDto>>> GetAll(CancellationToken ct)

    {

        var configs = await _repo.GetAllAsync(ct);

        return Ok(configs.Select(ToDto).ToList());

    }



    /// <summary>按 ID 查单条配置。</summary>

    [HttpGet("{id:long}")]

    [ProducesResponseType(typeof(FtpUploadConfigDto), StatusCodes.Status200OK)]

    [ProducesResponseType(StatusCodes.Status404NotFound)]

    public async Task<ActionResult<FtpUploadConfigDto>> GetById(long id, CancellationToken ct)

    {

        var config = await _repo.GetByIdAsync(id, ct);

        if (config is null) return NotFound(new { error = $"ID={id} 的配置不存在。" });

        return Ok(ToDto(config));

    }



    /// <summary>新增一条配置，ID 由数据库自动生成。FtpPassword 传明文直接存库。</summary>

    [HttpPost]

    [ProducesResponseType(typeof(CreateConfigResponse), StatusCodes.Status201Created)]

    [ProducesResponseType(StatusCodes.Status400BadRequest)]

    public async Task<ActionResult<CreateConfigResponse>> Create([FromBody] ConfigRequest req, CancellationToken ct)

    {

        if (string.IsNullOrWhiteSpace(req.FtpPassword))

            return BadRequest(new { error = "新增配置时 FtpPassword 不能为空。" });



        var entity = ToEntity(req);

        entity.FtpPassword = req.FtpPassword!.Trim();



        var newId = await _repo.InsertAsync(entity, ct);

        return CreatedAtAction(nameof(GetById), new { id = newId },

            new CreateConfigResponse { Id = newId });

    }



    /// <summary>更新配置。FtpPassword 传明文会覆盖；传空或 null 则保留原密码。</summary>

    [HttpPut("{id:long}")]

    [ProducesResponseType(StatusCodes.Status204NoContent)]

    [ProducesResponseType(StatusCodes.Status404NotFound)]

    public async Task<IActionResult> Update(long id, [FromBody] ConfigRequest req, CancellationToken ct)

    {

        var existing = await _repo.GetByIdAsync(id, ct);

        if (existing is null) return NotFound(new { error = $"ID={id} 的配置不存在。" });



        var entity = ToEntity(req);

        entity.Id = id;



        entity.FtpPassword = ShouldKeepExistingPassword(req.FtpPassword)

            ? string.Empty

            : req.FtpPassword!.Trim();



        var updated = await _repo.UpdateAsync(entity, ct);

        return updated ? NoContent() : NotFound(new { error = $"ID={id} 的配置不存在。" });

    }



    /// <summary>按 ID 删除配置（物理删除）。</summary>

    [HttpDelete("{id:long}")]

    [ProducesResponseType(StatusCodes.Status204NoContent)]

    [ProducesResponseType(StatusCodes.Status404NotFound)]

    public async Task<IActionResult> Delete(long id, CancellationToken ct)

    {

        var deleted = await _repo.DeleteAsync(id, ct);

        return deleted ? NoContent() : NotFound(new { error = $"ID={id} 的配置不存在。" });

    }



    private static bool ShouldKeepExistingPassword(string? password)

    {

        if (string.IsNullOrWhiteSpace(password))

            return true;

        var p = password.Trim();

        return p.Contains("***", StringComparison.Ordinal)

            || p.Contains("已设置", StringComparison.OrdinalIgnoreCase)

            || p.Contains("已加密", StringComparison.OrdinalIgnoreCase);

    }



    private static FtpUploadConfig ToEntity(ConfigRequest r) => new()

    {

        CustCode = r.CustCode,

        Device = r.Device,

        Cp = r.Cp,

        SourcePath = r.SourcePath,

        SourceKeyword = r.SourceKeyword,

        ProcessorName = r.ProcessorName,

        ProcessorParam = r.ProcessorParam,

        RenamerName = r.RenamerName,

        RenamerParam = r.RenamerParam,

        FtpProtocol = r.FtpProtocol.ToUpperInvariant(),

        FtpHost = r.FtpHost,

        FtpPort = r.FtpPort,

        FtpUser = r.FtpUser,

        FtpTargetPath = r.FtpTargetPath,

        Enabled = r.Enabled,

        Remark = r.Remark,

    };



    private static FtpUploadConfigDto ToDto(FtpUploadConfig c) => new()

    {

        Id = c.Id,

        CustCode = c.CustCode,

        Device = c.Device,

        Cp = c.Cp,

        SourcePath = c.SourcePath,

        SourceKeyword = c.SourceKeyword,

        ProcessorName = c.ProcessorName,

        ProcessorParam = c.ProcessorParam,

        RenamerName = c.RenamerName,

        RenamerParam = c.RenamerParam,

        FtpProtocol = c.FtpProtocol,

        FtpHost = c.FtpHost,

        FtpPort = c.FtpPort,

        FtpUser = c.FtpUser,

        FtpPassword = c.FtpPassword,

        FtpTargetPath = c.FtpTargetPath,

        Enabled = c.Enabled,

        Remark = c.Remark,

        CreatedAt = c.CreatedAt,

        UpdatedAt = c.UpdatedAt,

    };

}



/// <summary>查询返回的 DTO。</summary>

public class FtpUploadConfigDto

{

    public long Id { get; set; }

    public string CustCode { get; set; } = string.Empty;

    public string Device { get; set; } = string.Empty;

    public string Cp { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string SourceKeyword { get; set; } = string.Empty;

    public string ProcessorName { get; set; } = string.Empty;

    public string? ProcessorParam { get; set; }

    public string RenamerName { get; set; } = string.Empty;

    public string? RenamerParam { get; set; }

    public string FtpProtocol { get; set; } = string.Empty;

    public string FtpHost { get; set; } = string.Empty;

    public int FtpPort { get; set; }

    public string FtpUser { get; set; } = string.Empty;

    public string FtpPassword { get; set; } = string.Empty;

    public string FtpTargetPath { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string? Remark { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

}



public class CreateConfigResponse

{

    public long Id { get; set; }

}


