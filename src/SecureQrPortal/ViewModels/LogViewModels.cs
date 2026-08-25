using SecureQrPortal.Models;
namespace SecureQrPortal.ViewModels;
public sealed class AccessLogIndexVm
{
    public List<AccessLog> Items { get; set; } = [];
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public long? OrganizationId { get; set; }
    public long? SecurePageId { get; set; }
    public string? EventType { get; set; }
    public bool? Success { get; set; }
    public string? Ip { get; set; }
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int PageSize { get; set; } = 50;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
public sealed class AuditLogIndexVm
{
    public List<AuditLog> Items { get; set; } = [];
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int PageSize { get; set; } = 50;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
}
