using System.Text;

namespace AdElementsFree.Shortcuts;

public sealed record ArgumentToken(string Value, int Start, int Length);

public static class WindowsArguments
{
    // CommandLineToArgvW backslash/quote rules, retaining original spans for lossless edits.
    public static List<ArgumentToken> Parse(string input)
    {
        var result = new List<ArgumentToken>();
        int i = 0;
        while (i < input.Length)
        {
            while (i < input.Length && char.IsWhiteSpace(input[i])) i++;
            if (i == input.Length) break;
            int start = i;
            bool quoted = false;
            var value = new StringBuilder();
            while (i < input.Length && (quoted || !char.IsWhiteSpace(input[i])))
            {
                int slashes = 0;
                while (i < input.Length && input[i] == '\\') { slashes++; i++; }
                if (i < input.Length && input[i] == '"')
                {
                    value.Append('\\', slashes / 2);
                    if (slashes % 2 == 1) { value.Append('"'); i++; }
                    else if (quoted && i + 1 < input.Length && input[i + 1] == '"')
                    { value.Append('"'); i += 2; }
                    else { quoted = !quoted; i++; }
                }
                else
                {
                    value.Append('\\', slashes);
                    if (i < input.Length && (quoted || !char.IsWhiteSpace(input[i]))) value.Append(input[i++]);
                }
            }
            if (quoted) throw new FormatException("快捷方式参数包含未闭合引号，未修改。");
            result.Add(new(value.ToString(), start, i - start));
        }
        return result;
    }

    public static string EnsurePort(string input, int port, out bool added)
    {
        added = false;
        var tokens = Parse(input);
        var found = tokens.Where(t => t.Value.Equals("--remote-debugging-port", StringComparison.OrdinalIgnoreCase)
            || t.Value.StartsWith("--remote-debugging-port=", StringComparison.OrdinalIgnoreCase)).ToList();
        if (found.Count > 0)
        {
            if (found.Count != 1) throw new InvalidOperationException("存在多个调试端口参数，请手动检查。");
            var token = found[0];
            var value = token.Value.Contains('=') ? token.Value.Split('=', 2)[1]
                : tokens.ElementAtOrDefault(tokens.IndexOf(token) + 1)?.Value;
            if (value != port.ToString()) throw new InvalidOperationException("已有其他调试端口参数，未修改。");
            return input;
        }
        if (tokens.Any(t => t.Value == "--")) throw new InvalidOperationException("存在参数终止符，未修改。");
        added = true;
        return input + (input.Length == 0 || char.IsWhiteSpace(input[^1]) ? "" : " ") + $"--remote-debugging-port={port}";
    }

    public static string RemoveOwnedPort(string current, string original, string applied, int port)
    {
        if (current == applied) return original;
        var matches = Parse(current).Where(t => t.Value.Equals($"--remote-debugging-port={port}", StringComparison.Ordinal)).ToList();
        // Ambiguous user edits are left untouched.
        if (matches.Count != 1) return current;
        var token = matches[0];
        return current.Remove(token.Start, token.Length);
    }
}
