namespace SecureQrPortal.Tests;

public sealed class LocalizationP2BidiTests
{
    [Fact]
    public void Admin_layout_loads_localization_p2_bidi_styles()
    {
        var layout = File.ReadAllText(Path.Combine(RepoRoot(), "src", "SecureQrPortal", "Areas", "Admin", "Views", "Shared", "_AdminLayout.cshtml"));

        Assert.Contains("~/css/localization-p2.css", layout, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_bidi_styles_isolate_qr_and_technical_values()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "SecureQrPortal", "wwwroot", "css", "localization-p2.css"));

        Assert.Contains(".code", css, StringComparison.Ordinal);
        Assert.Contains(".secure-url-admin > span", css, StringComparison.Ordinal);
        Assert.Contains(".qr-registry-table td", css, StringComparison.Ordinal);
        Assert.Contains(".timeline-item small", css, StringComparison.Ordinal);
        Assert.Contains("direction: ltr", css, StringComparison.Ordinal);
        Assert.Contains("unicode-bidi: isolate", css, StringComparison.Ordinal);
        Assert.Contains("unicode-bidi: plaintext", css, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "SecureQrPortal.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Could not locate repository root from test output directory.");
    }
}
