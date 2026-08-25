using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SecureQrPortal.Data;
using SecureQrPortal.Services;
using SecureQrPortal.ViewModels;

namespace SecureQrPortal.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Administrator")]
public sealed class MobileDeliveryController(
    ApplicationDbContext db,
    MobileDeliveryAdminService deliveries) : Controller
{
    [HttpGet]
    public async Task<IActionResult> History(
        long? organizationId,
        long? securePageId,
        string? status,
        bool? opened,
        int page = 1,
        int pageSize = 20,
        string sort = "created_desc",
        CancellationToken ct = default)
    {
        var vm = await deliveries.HistoryAsync(organizationId, securePageId, status, opened, page, pageSize, sort, ct);
        ViewBag.Organizations = await db.Organizations.AsNoTracking().OrderBy(x => x.NameEnglish).ToListAsync(ct);
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken ct)
    {
        var vm = await deliveries.DetailsAsync(id, ct);
        return vm is null ? NotFound() : View(vm);
    }

    [HttpPost]
    public async Task<IActionResult> Send(MobileDeliverySendVm vm, CancellationToken ct)
    {
        DateTime? expiryUtc = null;
        if (vm.ExpiresAtLocal.HasValue)
            expiryUtc = vm.ExpiresAtLocal.Value.ToUniversalTime();

        var result = await deliveries.SendAsync(new MobileDeliverySendCommand(
            vm.SecurePageId,
            expiryUtc,
            vm.ReminderEnabled,
            vm.ReminderInterval,
            vm.ReminderUnit), ct);

        if (result.Success)
        {
            TempData["Success"] = "تم قبول الإرسال من مزود الإشعارات / Push provider accepted the send request.";
            return RedirectToAction(nameof(Details), new { id = result.DeliveryId });
        }

        TempData["Error"] = result.Code switch
        {
            "ORGANIZATION_INACTIVE" => "الجهة غير نشطة / Organization is inactive.",
            "SECURE_PAGE_NOT_ACTIVE" => "الصفحة الآمنة غير متاحة للإرسال / Secure Page is not currently active.",
            "ORGANIZATION_MOBILE_NOT_CONFIGURED" => "رقم الجوال غير مهيأ للجهة / Organization mobile number is not configured.",
            "NO_REGISTERED_DEVICE" => "لا يوجد جهاز DA Secure نشط ومستعد للإشعارات / No active DA Secure device is available for push.",
            "DELIVERY_EXPIRY_INVALID" => "تاريخ انتهاء الإرسال غير صالح / Delivery expiry is invalid.",
            "DELIVERY_EXPIRY_EXCEEDS_PAGE" => "انتهاء الإرسال لا يمكن أن يتجاوز انتهاء الصفحة الآمنة / Delivery expiry cannot exceed Secure Page expiry.",
            "REMINDER_INTERVAL_INVALID" or "REMINDER_INTERVAL_OUT_OF_RANGE" or "REMINDER_UNIT_INVALID" => "إعداد التذكير غير صالح / Reminder configuration is invalid.",
            "PROVIDER_UNAVAILABLE" => "مزود Firebase غير متاح حاليًا ولم يتم تسجيل نجاح وهمي / Firebase provider is unavailable; no success was recorded.",
            _ => $"تعذر الإرسال / Send failed: {result.Code}"
        };

        if (result.DeliveryId.HasValue)
            return RedirectToAction(nameof(Details), new { id = result.DeliveryId });
        return RedirectToAction("Details", "Qr", new { area = "Admin", id = vm.SecurePageId });
    }

    [HttpPost]
    public async Task<IActionResult> Revoke(long id, string confirmation, CancellationToken ct)
    {
        if (!string.Equals(confirmation, "REVOKE", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Error"] = "اكتب REVOKE لتأكيد الإلغاء / Type REVOKE to confirm revocation.";
            return RedirectToAction(nameof(Details), new { id });
        }

        var result = await deliveries.RevokeAsync(id, ct);
        if (!result.Success) return NotFound();
        TempData["Success"] = "تم إلغاء الإرسال وإيقاف التذكيرات المستقبلية منطقيًا / Delivery revoked and future reminders stopped logically.";
        return RedirectToAction(nameof(Details), new { id });
    }
}
