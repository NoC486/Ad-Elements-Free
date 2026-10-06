using System.Net;
using System.Text;
using AdElementsFree.Rules;

internal static class RuleChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var root = Path.Combine(AppContext.BaseDirectory, "rules-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        const string old = ".old { display:none; }";
        const string updated = ".new { display:none !important; }";
        var path = RuleFiles.PathFor(root, "KOOK");
        try
        {
            try { RuleFiles.Read("KOOK", root); throw new Exception("Missing rules accepted"); }
            catch (InvalidOperationException ex) { check(ex.Message.Contains("同步"), "Missing external CSS has actionable error"); }
            RuleSynchronizer.Install(root, new Dictionary<string, byte[]> { ["KOOK"] = Encoding.UTF8.GetBytes(old) });
            check(RuleFiles.Read("KOOK", root) == old, "Loads external client CSS");
            var handler = new RuleHandler(updated);
            using var http = new HttpClient(handler);
            var revision = await new RuleSynchronizer(http).SyncAsync(root);
            check(RuleFiles.Read("KOOK", root) == updated && File.ReadAllText(path + ".bak") == old, "Sync replaces CSS and preserves previous backup");
            check(revision == "aaaaaaa" && handler.Pinned, "Downloads use one fixed commit and client path");
            foreach (var response in new[] { "", "<!DOCTYPE html><html>error</html>", new string('x', 1024 * 1024 + 1) })
            {
                using var invalid = new HttpClient(new RuleHandler(response));
                try { await new RuleSynchronizer(invalid).SyncAsync(root); throw new Exception("Invalid CSS accepted"); }
                catch (InvalidDataException) { check(File.ReadAllText(path) == updated, "Invalid download leaves original CSS intact"); }
            }
            using var failed = new HttpClient(new RuleHandler("", HttpStatusCode.NotFound));
            try { await new RuleSynchronizer(failed).SyncAsync(root); throw new Exception("404 accepted"); }
            catch (HttpRequestException) { check(File.ReadAllText(path) == updated, "HTTP failure preserves CSS"); }
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await new RuleSynchronizer(http).SyncAsync(root, cancelled.Token); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { check(File.ReadAllText(path) == updated, "Cancelled sync preserves CSS"); }
            try { RuleFiles.PathFor(root, "../elsewhere"); throw new Exception("Traversal accepted"); }
            catch (ArgumentException) { check(true, "Only registered client folders allowed"); }
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { RuleSynchronizer.Install(root, new Dictionary<string, byte[]> { ["KOOK"] = Encoding.UTF8.GetBytes(old) }); throw new Exception("Locked CSS overwritten"); }
                catch (IOException) { check(true, "Locked rule replacement fails safely"); }
            }
            check(File.ReadAllText(path) == updated && !Directory.EnumerateFiles(root, "*.tmp", SearchOption.AllDirectories).Any(), "Failure leaves originals and removes staging files");
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class RuleHandler(string css, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public bool Pinned { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var url = request.RequestUri!.AbsoluteUri;
            if (url == "https://api.github.com/repos/NoC486/Ad-Elements-Free/commits/main")
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"sha\":\"" + new string('a', 40) + "\"}") });
            Pinned = url == "https://raw.githubusercontent.com/NoC486/Ad-Elements-Free/" + new string('a', 40) + "/src/AdElementsFree/Rules/KOOK/style.css";
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(css) });
        }
    }
}
