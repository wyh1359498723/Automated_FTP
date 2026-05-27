# Automated_FTP

由配置表驱动的 FTP / FTPS / SFTP 自动上传服务，基于 ASP.NET Core 8。

## 工作流程

API 调用者传入业务键 `(custCode, device, cp)` 与变量字典（如 `wflot`），后台：

1. 用业务键到 Oracle `FTP_UPLOAD_CONFIG` 表查唯一的启用配置
2. 用变量字典渲染源目录 / 关键词 / 目标目录中的占位符
3. 在源目录按关键词找文件
4. 调用配置指定的 `IFileProcessor` 处理文件
5. 调用配置指定的 `IFileRenamer` 生成目标文件名
6. 按配置 `FTP_PROTOCOL` 选择 FTP / FTPS / SFTP 客户端，连接并上传

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

主流程。Body：

```json
{
  "custCode": "C001",
  "device": "DEV01",
  "cp": "CP1",
  "variables": { "wflot": "WL202605270001" }
}
```

### `POST /api/diagnostics/file-check`

只渲染源目录、按关键词列文件。

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
