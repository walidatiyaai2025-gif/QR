using Ganss.Xss;

namespace SecureQrPortal.Services;

public sealed class HtmlContentService
{
    private readonly HtmlSanitizer _sanitizer = new();
    public HtmlContentService()
    {
        _sanitizer.AllowedAttributes.Add("class");
        _sanitizer.AllowedAttributes.Add("dir");
        _sanitizer.AllowedSchemes.Add("mailto");
    }
    public string Sanitize(string? html) => _sanitizer.Sanitize(html ?? string.Empty);
}
