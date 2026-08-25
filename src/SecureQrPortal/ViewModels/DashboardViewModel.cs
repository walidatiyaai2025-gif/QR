using SecureQrPortal.Models;
namespace SecureQrPortal.ViewModels;
public sealed class DashboardVm
{
 public int TotalPages {get;set;} public int ActivePages{get;set;} public int ExpiredPages{get;set;} public int DisabledPages{get;set;} public int Organizations{get;set;} public long ScansToday{get;set;} public long SuccessfulToday{get;set;} public long FailedToday{get;set;}
 public int TotalQr{get;set;} public int RevokedQr{get;set;} public int LimitReachedQr{get;set;} public long ScansMonth{get;set;}
 public List<(long Id,string Ref,string Organization,string Title,long Scans,long Success)> MostUsed {get;set;}=[];
 public List<(long Id,string Ref,string Organization,string Title,DateTime Expiry)> ExpiringSoon {get;set;}=[];
 public List<AccessLog> RecentActivity {get;set;}=[];
}
