using Automated_FTP.Models;

namespace Automated_FTP.Repositories;

public interface IConfigRepository
{
    /// <summary>按业务键查唯一配置；找不到或已禁用返回 null。</summary>
    Task<FtpUploadConfig?> FindByKeyAsync(string custCode, string device, string cp, CancellationToken ct = default);

    /// <summary>按 ID 查配置（含禁用行）。</summary>
    Task<FtpUploadConfig?> GetByIdAsync(long id, CancellationToken ct = default);

    /// <summary>列出所有配置行（含禁用）。</summary>
    Task<IReadOnlyList<FtpUploadConfig>> GetAllAsync(CancellationToken ct = default);

    /// <summary>插入一行，返回数据库生成的 ID。密码传入时已是密文。</summary>
    Task<long> InsertAsync(FtpUploadConfig config, CancellationToken ct = default);

    /// <summary>按 ID 更新。密码传入时已是密文（空字符串表示保留原密码）。</summary>
    Task<bool> UpdateAsync(FtpUploadConfig config, CancellationToken ct = default);

    /// <summary>按 ID 删除（物理删除）。</summary>
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}
