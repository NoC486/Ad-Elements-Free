using System.Net;
using System.Net.Http;
using AdElementsFree.Settings;
using AdElementsFree.Updates;
using Microsoft.Win32;

internal static class SettingsChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var keyPath = @"Software\AdElementsFree.Tests\" + Guid.NewGuid();
        var startup = new StartupRegistration(@"D:\Program Files\Ad Elements Free\AdElementsFree.exe", keyPath);
        try
        {
            check(!startup.IsEnabled(), "Startup defaults off without writing an entry");
            startup.SetEnabled(true);
            check(startup.IsEnabled(), "Startup registration roundtrip");
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath, true)!)
            {
                check((string?)key.GetValue(StartupRegistration.ValueName) == "\"D:\\Program Files\\Ad Elements Free\\AdElementsFree.exe\" --startup", "Startup path with spaces is quoted");
                key.SetValue("UnrelatedApp", "unchanged");
            }
            startup.SetEnabled(false);
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath)!)
                check(!startup.IsEnabled() && (string?)key.GetValue("UnrelatedApp") == "unchanged", "Disable only removes owned value");
            startup.SetEnabled(true);
            var otherCopy = new StartupRegistration(@"E:\Portable\AdElementsFree.exe", keyPath);
            check(!otherCopy.IsEnabled(), "Another copy does not appear enabled");
            otherCopy.SetEnabled(false);
            check(startup.IsEnabled(), "Another copy cannot remove owned startup entry");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath, false); }

        static string Release(string tag, bool draft = false, bool prerelease = false) =>
            System.Text.Json.JsonSerializer.Serialize(new { tag_name = tag, draft, prerelease, html_url = "https://untrusted.example" });
        var current = new Version(0, 2, 0, 0);
        var update = UpdateChecker.Parse(Release("v0.10.0"), current);
        check(update?.Version == new Version(0, 10, 0), "Versions compare numerically");
        check(update?.Url.AbsoluteUri == UpdateChecker.RepositoryUrl + "/releases/tag/v0.10.0", "Release URL constrained to our repository");
        check(UpdateChecker.Parse(Release("v0.2.0"), current) == null, "Equal three/four part versions not treated as updates");
        check(UpdateChecker.Parse(Release("v0.1.1"), current) == null, "Older release not offered");
        foreach (var json in new[] { Release("v1.0.0", draft: true), Release("v1.0.0", prerelease: true), Release("v1.0.0-beta"), Release("../../bad") })
        {
            try { UpdateChecker.Parse(json, current); throw new Exception("Invalid release accepted"); }
            catch (InvalidDataException) { check(true, "Reject draft, prerelease or malformed release"); }
        }
        var handler = new Handler(HttpStatusCode.OK, Release("v0.3.0"));
        using var client = new HttpClient(handler);
        check((await new UpdateChecker(client).CheckAsync(current))?.Version == new Version(0, 3, 0) && handler.ValidRequest, "Manual request endpoint and headers");
        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.TooManyRequests, HttpStatusCode.NotFound })
        {
            using var failureClient = new HttpClient(new Handler(status, "{}"));
            try { await new UpdateChecker(failureClient).CheckAsync(current); throw new Exception("HTTP failure accepted"); }
            catch (InvalidDataException) { check(true, "HTTP failure shown separately from up-to-date"); }
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await new UpdateChecker(client).CheckAsync(current, cancelled.Token); throw new Exception("Cancellation ignored"); }
        catch (OperationCanceledException) { check(true, "Closing settings cancels update request"); }
    }

    private sealed class Handler(HttpStatusCode status, string content) : HttpMessageHandler
    {
        public bool ValidRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            ValidRequest = request.RequestUri?.AbsoluteUri == "https://api.github.com/repos/NoC486/Ad-Elements-Free/releases/latest"
                && request.Headers.UserAgent.Count > 0 && request.Headers.Contains("X-GitHub-Api-Version");
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(content) });
        }
    }
}
