namespace SecureQrPortal.Tests;

public sealed class LocalizationP2SourceCoverageTests
{
    [Fact]
    public void Admin_qr_surfaces_apply_bidi_isolation_to_references_urls_timestamps_and_timeline_values()
    {
        var qrIndex = Read("src", "SecureQrPortal", "Areas", "Admin", "Views", "Qr", "Index.cshtml");
        var qrDetails = Read("src", "SecureQrPortal", "Areas", "Admin", "Views", "Qr", "Details.cshtml");
        var p2Css = Read("src", "SecureQrPortal", "wwwroot", "css", "localization-p2.css");

        Assert.Contains("class=\"code\"", qrIndex, StringComparison.Ordinal);
        Assert.Contains("qr-registry-table", qrIndex, StringComparison.Ordinal);
        Assert.Contains("secure-url-admin", qrDetails, StringComparison.Ordinal);
        Assert.Contains("<dl class=\"kv\">", qrDetails, StringComparison.Ordinal);
        Assert.Contains("timeline-item", qrDetails, StringComparison.Ordinal);

        Assert.Contains(".code", p2Css, StringComparison.Ordinal);
        Assert.Contains(".secure-url-admin > span", p2Css, StringComparison.Ordinal);
        Assert.Contains(".qr-registry-table td", p2Css, StringComparison.Ordinal);
        Assert.Contains(".kv dd", p2Css, StringComparison.Ordinal);
        Assert.Contains(".timeline-item small", p2Css, StringComparison.Ordinal);
        Assert.Contains("unicode-bidi: isolate", p2Css, StringComparison.Ordinal);
        Assert.Contains("unicode-bidi: plaintext", p2Css, StringComparison.Ordinal);
    }

    [Fact]
    public void Admin_access_and_organization_surfaces_isolate_ip_phone_and_timestamp_values()
    {
        var access = Read("src", "SecureQrPortal", "Areas", "Admin", "Views", "Logs", "Access.cshtml");
        var organizations = Read("src", "SecureQrPortal", "Areas", "Admin", "Views", "Organizations", "Index.cshtml");
        var adminCss = Read("src", "SecureQrPortal", "wwwroot", "css", "admin-closure.css");

        Assert.Contains("x.TimestampUtc.ToLocalTime()", access, StringComparison.Ordinal);
        Assert.Contains("class=\"numeric\">@(x.IpAddress", access, StringComparison.Ordinal);
        Assert.Contains("class=\"code\" dir=\"ltr\"", access, StringComparison.Ordinal);

        Assert.Contains("dir=\"ltr\"", organizations, StringComparison.Ordinal);
        Assert.Contains("class=\"numeric\">@Mobile(o.MobileNumber)", organizations, StringComparison.Ordinal);
        Assert.Contains("o.CreatedAtUtc.ToLocalTime()", organizations, StringComparison.Ordinal);

        Assert.Contains(".admin-content .numeric{direction:ltr;unicode-bidi:isolate", adminCss, StringComparison.Ordinal);
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot() }.Concat(parts).ToArray()));

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
