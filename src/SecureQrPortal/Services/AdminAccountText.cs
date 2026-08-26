namespace SecureQrPortal.Services;

public static class AdminAccountText
{
    public static string ChangePasswordValidation(bool arabic) => arabic
        ? "راجع حقول كلمة المرور وصحح أخطاء التحقق."
        : "Review the password fields and correct the validation errors.";

    public static string PasswordChanged(bool arabic) => arabic
        ? "تم تغيير كلمة المرور بنجاح."
        : "Password changed successfully.";

    public static string ChangePasswordError(string code, bool arabic) => code switch
    {
        "PasswordMismatch" => arabic ? "كلمة المرور الحالية غير صحيحة." : "The current password is incorrect.",
        "PasswordTooShort" => arabic ? "كلمة المرور الجديدة أقصر من الحد الأدنى المطلوب." : "The new password is shorter than the required minimum length.",
        "PasswordRequiresNonAlphanumeric" => arabic ? "يجب أن تحتوي كلمة المرور الجديدة على رمز خاص واحد على الأقل." : "The new password must contain at least one non-alphanumeric character.",
        "PasswordRequiresDigit" => arabic ? "يجب أن تحتوي كلمة المرور الجديدة على رقم واحد على الأقل." : "The new password must contain at least one digit.",
        "PasswordRequiresLower" => arabic ? "يجب أن تحتوي كلمة المرور الجديدة على حرف إنجليزي صغير واحد على الأقل." : "The new password must contain at least one lowercase letter.",
        "PasswordRequiresUpper" => arabic ? "يجب أن تحتوي كلمة المرور الجديدة على حرف إنجليزي كبير واحد على الأقل." : "The new password must contain at least one uppercase letter.",
        "PasswordRequiresUniqueChars" => arabic ? "كلمة المرور الجديدة لا تحتوي على عدد كافٍ من الأحرف المختلفة." : "The new password does not contain enough unique characters.",
        _ => arabic ? "تعذر تحديث كلمة المرور. راجع المتطلبات وحاول مرة أخرى." : "Unable to update the password. Review the requirements and try again."
    };
}
