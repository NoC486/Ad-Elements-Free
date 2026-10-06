using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AdElementsFree.Updates;

public sealed record ReleaseUpdate(Version Version, Uri Url);

public sealed class UpdateChecker
{
    public const string RepositoryUrl = "https://github.com/NoC486/Ad-Elements-Free";
    public static readonly Version CurrentVersion = new(typeof(UpdateChecker).Assembly.GetName().Version!.ToString(3));
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 1024 * 1024 };
    private readonly HttpClient client;
    public UpdateChecker(HttpClient? client = null) => this.client = client ?? Client;

    public async Task<ReleaseUpdate?> CheckAsync(Version current, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/NoC486/Ad-Elements-Free/releases/latest");
        request.Headers.UserAgent.ParseAdd($"AdElementsFree/{CurrentVersion}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidDataException("暂无可用的正式版本，请稍后重试。");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidDataException("GitHub 暂时限制了请求，请稍后重试。");
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(cancellationToken), current);
    }

    public static ReleaseUpdate? Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean())
            throw new InvalidDataException("未获取到可用的正式版本。");
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$", RegexOptions.CultureInvariant)
            || !Version.TryParse(tag.TrimStart('v'), out var version))
            throw new InvalidDataException("无法识别发布版本，请到 GitHub 查看。");
        // Build our own repository URL; never launch a URL supplied in the response.
        return version > new Version(current.Major, current.Minor, Math.Max(0, current.Build))
            ? new(version, new Uri($"{RepositoryUrl}/releases/tag/{tag}")) : null;
    }
}
