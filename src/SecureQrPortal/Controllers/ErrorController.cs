using System.Globalization;
using Microsoft.AspNetCore.Mvc;

namespace SecureQrPortal.Controllers;

[Route("error")]
public sealed class ErrorController : Controller
{
    private static bool IsArabic => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ar";

    [Route("forbidden")]
    public IActionResult Forbidden()
    {
        Response.StatusCode = 403;
        return View("Error", IsArabic
            ? ("403", "غير مسموح", "ليس لديك صلاحية لتنفيذ هذا الإجراء.")
            : ("403", "Forbidden", "You do not have permission to perform this action."));
    }

    [Route("500")]
    public IActionResult ServerError()
    {
        Response.StatusCode = 500;
        return View("Error", IsArabic
            ? ("500", "خطأ غير متوقع", "تعذر إكمال الطلب. حاول مرة أخرى أو تواصل مع مسؤول النظام.")
            : ("500", "Unexpected error", "The request could not be completed. Try again or contact the system administrator."));
    }

    [Route("404")]
    public IActionResult NotFoundPage()
    {
        Response.StatusCode = 404;
        return View("Error", IsArabic
            ? ("404", "غير موجود", "لم يتم العثور على المورد المطلوب.")
            : ("404", "Not found", "The requested resource was not found."));
    }
}
