using System.Data;
using Automated_FTP.Models;
using Dapper;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace Automated_FTP.Repositories;

public class OracleOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

public class UploadOptions
{
    public string ConfigTableName { get; set; } = "MMS_FTP_UPLOAD_CONFIG";
    public int DefaultFtpTimeoutSeconds { get; set; } = 30;
}

public class OracleConfigRepository : IConfigRepository
{
    private readonly OracleOptions _oracle;
    private readonly UploadOptions _upload;
    private readonly ILogger<OracleConfigRepository> _logger;

    public OracleConfigRepository(
        IOptions<OracleOptions> oracle,
        IOptions<UploadOptions> upload,
        ILogger<OracleConfigRepository> logger)
    {
        _oracle = oracle.Value;
        _upload = upload.Value;
        _logger = logger;
    }

    private string T => _upload.ConfigTableName;

    private OracleConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_oracle.ConnectionString))
            throw new InvalidOperationException("Oracle:ConnectionString 未配置。");
        return new OracleConnection(_oracle.ConnectionString);
    }

    // ────────────────────────────────────────────────────────────────────────
    // SELECT helpers
    // ────────────────────────────────────────────────────────────────────────

    private static string SelectColumns(string table) => $@"
SELECT
    ID                AS Id,
    CUST_CODE         AS CustCode,
    DEVICE            AS Device,
    CP                AS Cp,
    SOURCE_PATH       AS SourcePath,
    SOURCE_KEYWORD    AS SourceKeyword,
    PROCESSOR_NAME    AS ProcessorName,
    PROCESSOR_PARAM   AS ProcessorParam,
    RENAMER_NAME      AS RenamerName,
    RENAMER_PARAM     AS RenamerParam,
    FTP_PROTOCOL      AS FtpProtocol,
    FTP_HOST          AS FtpHost,
    FTP_PORT          AS FtpPort,
    FTP_USER          AS FtpUser,
    FTP_PASSWORD      AS FtpPassword,
    FTP_TARGET_PATH   AS FtpTargetPath,
    ENABLED           AS Enabled,
    REMARK            AS Remark,
    CREATED_AT        AS CreatedAt,
    UPDATED_AT        AS UpdatedAt
FROM {table}";

    // ────────────────────────────────────────────────────────────────────────
    // IConfigRepository
    // ────────────────────────────────────────────────────────────────────────

    public async Task<FtpUploadConfig?> FindByKeyAsync(string custCode, string device, string cp, CancellationToken ct = default)
    {
        var sql = SelectColumns(T) + " WHERE CUST_CODE = :custCode AND DEVICE = :device AND CP = :cp AND ENABLED = 1";
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ConfigRow>(
            new CommandDefinition(sql, new { custCode, device, cp }, cancellationToken: ct));
        if (row is null)
            _logger.LogInformation("未找到启用的配置 custCode={CustCode} device={Device} cp={Cp}", custCode, device, cp);
        return row?.ToEntity();
    }

    public async Task<FtpUploadConfig?> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var sql = SelectColumns(T) + " WHERE ID = :id";
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var row = await conn.QuerySingleOrDefaultAsync<ConfigRow>(
            new CommandDefinition(sql, new { id }, cancellationToken: ct));
        return row?.ToEntity();
    }

    public async Task<IReadOnlyList<FtpUploadConfig>> GetAllAsync(CancellationToken ct = default)
    {
        var sql = SelectColumns(T) + " ORDER BY ID";
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var rows = await conn.QueryAsync<ConfigRow>(new CommandDefinition(sql, cancellationToken: ct));
        return rows.Select(r => r.ToEntity()).ToList();
    }

    public async Task<long> InsertAsync(FtpUploadConfig config, CancellationToken ct = default)
    {
        var sql = $@"
INSERT INTO {T} (
    CUST_CODE, DEVICE, CP,
    SOURCE_PATH, SOURCE_KEYWORD,
    PROCESSOR_NAME, PROCESSOR_PARAM,
    RENAMER_NAME, RENAMER_PARAM,
    FTP_PROTOCOL, FTP_HOST, FTP_PORT, FTP_USER, FTP_PASSWORD, FTP_TARGET_PATH,
    ENABLED, REMARK
) VALUES (
    :custCode, :device, :cp,
    :sourcePath, :sourceKeyword,
    :processorName, :processorParam,
    :renamerName, :renamerParam,
    :ftpProtocol, :ftpHost, :ftpPort, :ftpUser, :ftpPassword, :ftpTargetPath,
    :enabled, :remark
) RETURNING ID INTO :newId";

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        cmd.Parameters.Add("custCode",      OracleDbType.Varchar2).Value = config.CustCode;
        cmd.Parameters.Add("device",        OracleDbType.Varchar2).Value = config.Device;
        cmd.Parameters.Add("cp",            OracleDbType.Varchar2).Value = config.Cp;
        cmd.Parameters.Add("sourcePath",    OracleDbType.Varchar2).Value = config.SourcePath;
        cmd.Parameters.Add("sourceKeyword", OracleDbType.Varchar2).Value = config.SourceKeyword;
        cmd.Parameters.Add("processorName", OracleDbType.Varchar2).Value = config.ProcessorName;
        cmd.Parameters.Add("processorParam",OracleDbType.Varchar2).Value = (object?)config.ProcessorParam ?? DBNull.Value;
        cmd.Parameters.Add("renamerName",   OracleDbType.Varchar2).Value = config.RenamerName;
        cmd.Parameters.Add("renamerParam",  OracleDbType.Varchar2).Value = (object?)config.RenamerParam ?? DBNull.Value;
        cmd.Parameters.Add("ftpProtocol",   OracleDbType.Varchar2).Value = config.FtpProtocol;
        cmd.Parameters.Add("ftpHost",       OracleDbType.Varchar2).Value = config.FtpHost;
        cmd.Parameters.Add("ftpPort",       OracleDbType.Int32   ).Value = config.FtpPort;
        cmd.Parameters.Add("ftpUser",       OracleDbType.Varchar2).Value = config.FtpUser;
        cmd.Parameters.Add("ftpPassword",   OracleDbType.Varchar2).Value = config.FtpPassword;
        cmd.Parameters.Add("ftpTargetPath", OracleDbType.Varchar2).Value = config.FtpTargetPath;
        cmd.Parameters.Add("enabled",       OracleDbType.Int32   ).Value = config.Enabled ? 1 : 0;
        cmd.Parameters.Add("remark",        OracleDbType.Varchar2).Value = (object?)config.Remark ?? DBNull.Value;

        var outParam = cmd.Parameters.Add("newId", OracleDbType.Int64);
        outParam.Direction = ParameterDirection.Output;

        await cmd.ExecuteNonQueryAsync(ct);

        var newId = Convert.ToInt64(outParam.Value.ToString());
        _logger.LogInformation("已插入配置 ID={Id} ({CustCode}/{Device}/{Cp})", newId, config.CustCode, config.Device, config.Cp);
        return newId;
    }

    public async Task<bool> UpdateAsync(FtpUploadConfig config, CancellationToken ct = default)
    {
        // 密码若为空字符串表示"不改密码"，用子查询保留原值
        var pwdClause = string.IsNullOrEmpty(config.FtpPassword)
            ? "FTP_PASSWORD = (SELECT FTP_PASSWORD FROM " + T + " WHERE ID = :id)"
            : "FTP_PASSWORD = :ftpPassword";

        var sql = $@"
UPDATE {T} SET
    CUST_CODE       = :custCode,
    DEVICE          = :device,
    CP              = :cp,
    SOURCE_PATH     = :sourcePath,
    SOURCE_KEYWORD  = :sourceKeyword,
    PROCESSOR_NAME  = :processorName,
    PROCESSOR_PARAM = :processorParam,
    RENAMER_NAME    = :renamerName,
    RENAMER_PARAM   = :renamerParam,
    FTP_PROTOCOL    = :ftpProtocol,
    FTP_HOST        = :ftpHost,
    FTP_PORT        = :ftpPort,
    FTP_USER        = :ftpUser,
    {pwdClause},
    FTP_TARGET_PATH = :ftpTargetPath,
    ENABLED         = :enabled,
    REMARK          = :remark,
    UPDATED_AT      = SYSTIMESTAMP
WHERE ID = :id";

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;

        cmd.Parameters.Add("custCode",      OracleDbType.Varchar2).Value = config.CustCode;
        cmd.Parameters.Add("device",        OracleDbType.Varchar2).Value = config.Device;
        cmd.Parameters.Add("cp",            OracleDbType.Varchar2).Value = config.Cp;
        cmd.Parameters.Add("sourcePath",    OracleDbType.Varchar2).Value = config.SourcePath;
        cmd.Parameters.Add("sourceKeyword", OracleDbType.Varchar2).Value = config.SourceKeyword;
        cmd.Parameters.Add("processorName", OracleDbType.Varchar2).Value = config.ProcessorName;
        cmd.Parameters.Add("processorParam",OracleDbType.Varchar2).Value = (object?)config.ProcessorParam ?? DBNull.Value;
        cmd.Parameters.Add("renamerName",   OracleDbType.Varchar2).Value = config.RenamerName;
        cmd.Parameters.Add("renamerParam",  OracleDbType.Varchar2).Value = (object?)config.RenamerParam ?? DBNull.Value;
        cmd.Parameters.Add("ftpProtocol",   OracleDbType.Varchar2).Value = config.FtpProtocol;
        cmd.Parameters.Add("ftpHost",       OracleDbType.Varchar2).Value = config.FtpHost;
        cmd.Parameters.Add("ftpPort",       OracleDbType.Int32   ).Value = config.FtpPort;
        cmd.Parameters.Add("ftpUser",       OracleDbType.Varchar2).Value = config.FtpUser;
        if (!string.IsNullOrEmpty(config.FtpPassword))
            cmd.Parameters.Add("ftpPassword", OracleDbType.Varchar2).Value = config.FtpPassword;
        cmd.Parameters.Add("ftpTargetPath", OracleDbType.Varchar2).Value = config.FtpTargetPath;
        cmd.Parameters.Add("enabled",       OracleDbType.Int32   ).Value = config.Enabled ? 1 : 0;
        cmd.Parameters.Add("remark",        OracleDbType.Varchar2).Value = (object?)config.Remark ?? DBNull.Value;
        cmd.Parameters.Add("id",            OracleDbType.Int64   ).Value = config.Id;

        var affected = await cmd.ExecuteNonQueryAsync(ct);
        return affected > 0;
    }

    public async Task<bool> DeleteAsync(long id, CancellationToken ct = default)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);
        var affected = await conn.ExecuteAsync(
            new CommandDefinition($"DELETE FROM {T} WHERE ID = :id", new { id }, cancellationToken: ct));
        return affected > 0;
    }

    /// <summary>
    /// Dapper 行映射类型：ENABLED 在 Oracle 是 NUMBER(1)，先映射为 int 再转 bool。
    /// </summary>
    private class ConfigRow
    {
        public long Id { get; set; }
        public string CustCode { get; set; } = string.Empty;
        public string Device { get; set; } = string.Empty;
        public string Cp { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string SourceKeyword { get; set; } = string.Empty;
        public string ProcessorName { get; set; } = "PassThrough";
        public string? ProcessorParam { get; set; }
        public string RenamerName { get; set; } = "Template";
        public string? RenamerParam { get; set; }
        public string FtpProtocol { get; set; } = "FTP";
        public string FtpHost { get; set; } = string.Empty;
        public int FtpPort { get; set; } = 21;
        public string FtpUser { get; set; } = string.Empty;
        public string FtpPassword { get; set; } = string.Empty;
        public string FtpTargetPath { get; set; } = string.Empty;
        public int Enabled { get; set; }
        public string? Remark { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public FtpUploadConfig ToEntity() => new()
        {
            Id = Id,
            CustCode = CustCode,
            Device = Device,
            Cp = Cp,
            SourcePath = SourcePath,
            SourceKeyword = SourceKeyword,
            ProcessorName = ProcessorName,
            ProcessorParam = ProcessorParam,
            RenamerName = RenamerName,
            RenamerParam = RenamerParam,
            FtpProtocol = FtpProtocol,
            FtpHost = FtpHost,
            FtpPort = FtpPort,
            FtpUser = FtpUser,
            FtpPassword = FtpPassword,
            FtpTargetPath = FtpTargetPath,
            Enabled = Enabled == 1,
            Remark = Remark,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
    }
}
