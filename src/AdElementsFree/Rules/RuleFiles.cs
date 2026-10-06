using System.Text;

namespace AdElementsFree.Rules;

public static class RuleFiles
{
    // Add a folder here when a new compiled provider is registered.
    public static IReadOnlyList<string> Clients { get; } = Array.AsReadOnly(new[] { "KOOK" });
    public static string PathFor(string root, string client)
    {
        if (!Clients.Contains(client, StringComparer.Ordinal)) throw new ArgumentException("Unknown rules folder.", nameof(client));
        return Path.Combine(root, "Rules", client, "style.css");
    }

    public static string Read(string client, string? root = null)
    {
        try { return Validate(File.ReadAllBytes(PathFor(root ?? AppContext.BaseDirectory, client))); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or DecoderFallbackException)
        { throw new InvalidOperationException($"无法读取 Rules/{client}/style.css，请在设置中同步最新规则，或检查文件权限与内容。", ex); }
    }

    public static string Validate(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 1024 * 1024) throw new InvalidDataException("规则文件为空或超过 1 MB。");
        var css = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        if (string.IsNullOrWhiteSpace(css) || css.Contains('\0') || css.TrimStart().StartsWith('<'))
            throw new InvalidDataException("服务器没有返回有效的 CSS 文本。");
        return css;
    }
}
