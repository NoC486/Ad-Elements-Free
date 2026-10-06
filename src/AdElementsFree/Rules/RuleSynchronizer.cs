using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AdElementsFree.Rules;

public sealed class RuleSynchronizer
{
    private static readonly HttpClient SharedClient = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 1024 * 1024 };
    private readonly HttpClient client;
    public RuleSynchronizer(HttpClient? client = null) => this.client = client ?? SharedClient;

    public async Task<string> SyncAsync(string root, CancellationToken token = default)
    {
        // Pin the entire download to one commit so clients cannot receive mixed revisions.
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/NoC486/Ad-Elements-Free/commits/main");
        request.Headers.UserAgent.ParseAdd("AdElementsFree-Rules/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, token);
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var sha = document.RootElement.GetProperty("sha").GetString() ?? "";
        if (!Regex.IsMatch(sha, "^[0-9a-f]{40}$")) throw new InvalidDataException("无法识别规则版本。");
        var files = new Dictionary<string, byte[]>();
        foreach (var name in RuleFiles.Clients)
        {
            using var download = new HttpRequestMessage(HttpMethod.Get,
                $"https://raw.githubusercontent.com/NoC486/Ad-Elements-Free/{sha}/src/AdElementsFree/Rules/{name}/style.css");
            download.Headers.UserAgent.ParseAdd("AdElementsFree-Rules/1.0");
            using var result = await client.SendAsync(download, token);
            result.EnsureSuccessStatusCode();
            var bytes = await result.Content.ReadAsByteArrayAsync(token);
            RuleFiles.Validate(bytes);
            files.Add(name, bytes);
        }
        token.ThrowIfCancellationRequested();
        Install(root, files);
        return sha[..7];
    }

    public static void Install(string root, IReadOnlyDictionary<string, byte[]> files)
    {
        // Prepare every file before replacing any original; keep previous CSS as style.css.bak.
        var staged = new List<(string Path, string Temp, byte[]? Previous)>();
        var replaced = new List<(string Path, byte[]? Previous)>();
        try
        {
            foreach (var (name, bytes) in files)
            {
                RuleFiles.Validate(bytes);
                var path = RuleFiles.PathFor(root, name);
                RejectLinks(root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                staged.Add((path, temp, previous));
                File.WriteAllBytes(temp, bytes);
            }
            foreach (var item in staged)
            {
                if (item.Previous != null) File.WriteAllBytes(item.Path + ".bak", item.Previous);
                File.Move(item.Temp, item.Path, true);
                replaced.Add((item.Path, item.Previous));
            }
        }
        catch
        {
            foreach (var item in replaced.AsEnumerable().Reverse())
            {
                if (item.Previous == null) File.Delete(item.Path);
                else File.WriteAllBytes(item.Path, item.Previous);
            }
            throw;
        }
        finally
        {
            foreach (var item in staged)
                try { File.Delete(item.Temp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static void RejectLinks(string root, string path)
    {
        // The elevated helper only writes its own fixed Rules tree, never through junctions.
        foreach (var entry in new[] { root, Path.Combine(root, "Rules"), Path.GetDirectoryName(path)!, path, path + ".bak" })
            if ((File.Exists(entry) || Directory.Exists(entry)) && (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("规则目录不能使用符号链接或目录联接。");
    }
}
