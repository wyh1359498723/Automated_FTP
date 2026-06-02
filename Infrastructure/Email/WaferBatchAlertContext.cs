namespace Automated_FTP.Infrastructure.Email;

public class WaferBatchAlertContext
{
    public string CustCode { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;
    public string Cp { get; set; } = string.Empty;
    public long ConfigId { get; set; }
    public string? ConfigRemark { get; set; }
    public string WaferNos { get; set; } = string.Empty;
    public IReadOnlyList<string> MissingWaferNos { get; set; } = Array.Empty<string>();
    public IReadOnlyList<WaferScanDetail> WaferDetails { get; set; } = Array.Empty<WaferScanDetail>();
}

public class WaferScanDetail
{
    public string WfNo { get; set; } = string.Empty;
    public string RenderedSourcePath { get; set; } = string.Empty;
    public string Keyword { get; set; } = string.Empty;
    public int MatchedFileCount { get; set; }
}
