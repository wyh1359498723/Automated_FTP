# Automated_FTP

由配置表驱动的 FTP / FTPS / SFTP 自动上传服务，基于 ASP.NET Core 8。

## 工作流程

API 调用者传入业务键 `(custCode, device, cp)` 与变量字典（如 `wflot`），后台：

1. 用业务键到 Oracle `MMS_FTP_UPLOAD_CONFIG` 表查**所有**启用配置（同一业务键可有多条）
2. 按 ID 顺序逐条执行：渲染路径 → 找文件 → 处理 → 改名 → 上传

## 依赖

- `Oracle.ManagedDataAccess.Core` —— 连 Oracle
- `Dapper` —— 配置表查询
- `FluentFTP` —— FTP / FTPS
- `SSH.NET` —— SFTP
- `Microsoft.AspNetCore.DataProtection` —— FTP 密码加解密

## 配置

`appsettings.json`：

```json
{
  "Oracle": {
    "ConnectionString": "User Id=...;Password=...;Data Source=...;"
  },
  "DataProtection": {
    "KeysPath": "App_Data/DataProtection-Keys",
    "ApplicationName": "Automated_FTP"
  },
  "Upload": {
    "ConfigTableName": "FTP_UPLOAD_CONFIG",
    "DefaultFtpTimeoutSeconds": 30
  }
}
```

> `DataProtection:KeysPath` 必须可写、跨进程稳定。生产环境建议使用相对路径或绝对路径，**不要随意删除**该目录，否则原先加密的 FTP 密码将无法解密。

## 数据表

建表脚本：[Database/FTP_UPLOAD_CONFIG.sql](Database/FTP_UPLOAD_CONFIG.sql)。

## API

### `POST /api/upload`

主流程。按 `(custCode, device, cp)` 匹配**所有启用配置**并依次上传。Body：

```json
{
  "custCode": "C001",
  "device": "DEV01",
  "cp": "CP1",
  "variables": {
    "wflot": "WL202605270001",
    "lotid": "LOT001",
    "ENGorMP": "MP",
    "wf_id": "1",
    "testFlag": "Y",
    "testTime": "20260527143000"
  },
  "configIds": [3, 7],
  "waferNos": "1,2,3,4"
}
```

`configIds` 可选：不传或空数组 = 执行该业务键下全部启用配置；传 ID 数组 = 只执行指定配置（须属于该业务键且已启用）。

`waferNos` 可选：逗号分隔片号组（纯数字自动补零为两位，如 `1,2,3` → `{wf_no}` 依次为 `01,02,03`）。也可在 `variables.wf_nos` 传入；`{wf_nos}` 为规范化后的逗号串。

**片号组完整性**：传了 `waferNos` 时，每个片号都必须至少匹配到一个文件，否则**整批取消 FTP 上传**并发送邮件报警（见 `EmailAlert` 配置）。

### `POST /api/diagnostics/file-check`

只渲染源目录、按关键词列文件。未传 `waferNos` 时：渲染后的源目录必须存在且至少匹配 1 个文件，否则 `success=false`（目录不存在时不会抛异常，只返回空列表）。传了 `waferNos` 时按片号组完整性校验。

### `POST /api/diagnostics/ftp-check`

只测 FTP 连接和目标目录可达性。

### `POST /api/diagnostics/protect-password`

把明文密码转密文，DBA/运维拿到结果后写入 `FTP_PASSWORD` 字段。Body：

```json
{ "plainText": "your-plain-password" }
```

## 占位符

可在 `SOURCE_PATH` / `SOURCE_KEYWORD` / `FTP_TARGET_PATH` / `RENAMER_PARAM` 中使用：

| 占位符 | 来源 |
|---|---|
| `{custCode}`、`{device}`、`{cp}` | API 请求 / 配置 |
| `{wflot}` 等任意自定义键 | API 请求 `variables` |
| `{originalName}`、`{originalNameNoExt}`、`{ext}` | 源文件名（仅改名模板生效） |
| `{yyyyMMdd}`、`{yyyy}`、`{MM}`、`{dd}` | 系统时间 |
| `{HHmmss}`、`{HH}`、`{mm}`、`{ss}` | 系统时间 |

> 占位符大小写不敏感。

## 扩展点

### 新增"文件处理方法"

1. 在 `Services/Processors/` 下新增类，实现 `IFileProcessor`，给一个唯一的 `Name`
2. 在 [Program.cs](Program.cs) 注册：`services.AddSingleton<IFileProcessor, YourProcessor>()`
3. 配置表 `PROCESSOR_NAME` 写上对应的 `Name` 即可

### 新增"改名规则"

同上，针对 `IFileRenamer` 和 `Services/Renamers/`。

## 项目结构

```
Controllers/
  UploadController.cs
  DiagnosticsController.cs
Models/
  FtpUploadConfig.cs / UploadRequest.cs / UploadResult.cs
Repositories/
  IConfigRepository.cs / OracleConfigRepository.cs
Services/
  IFtpUploadService.cs / FtpUploadService.cs
  PathTemplateRenderer.cs / FileFinder.cs
  Processors/  IFileProcessor / FileProcessorRegistry / PassThroughProcessor
  Renamers/    IFileRenamer / FileRenamerRegistry / TemplateRenamer / KeepOriginalRenamer
  Ftp/         IFileTransferClient / FtpTransferClient / SftpTransferClient / FileTransferClientFactory
Infrastructure/Security/
  PasswordProtector.cs
Database/
  FTP_UPLOAD_CONFIG.sql
```
