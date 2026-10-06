using System.Reflection;
using System.Text.Json;

namespace AdElementsFree.Providers.KOOK;

public static class KookRules
{
    public const string StyleId = "ad-elements-free-kook";
    public static string Build(string pageUrl)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AdElementsFree.Providers.KOOK.Rules.style.css")!;
        using var reader = new StreamReader(stream);
        var css = JsonSerializer.Serialize(reader.ReadToEnd());
        // Every execution (including reloads and child frames) checks the pinned origin/path.
        return $$"""
        (() => {
          const expected = new URL({{JsonSerializer.Serialize(pageUrl)}});
          if (window.top !== window || location.protocol !== expected.protocol || location.host !== expected.host ||
              location.pathname !== expected.pathname) return false;
          const apply = () => {
            if (!document.head) return false;
            if (!document.getElementById('{{StyleId}}')) {
              const style = document.createElement('style'); style.id = '{{StyleId}}';
              style.textContent = {{css}}; document.head.appendChild(style);
            }
            window.__aef_kook_rules_loaded = true;
            return true;
          };
          if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', apply, {once:true}); return false;
          }
          return apply();
        })()
        """;
    }
    public static string Remove(string pageUrl) => $$"""
        (() => {
          const expected = new URL({{JsonSerializer.Serialize(pageUrl)}});
          if (location.protocol !== expected.protocol || location.host !== expected.host || location.pathname !== expected.pathname) return;
          document.getElementById('{{StyleId}}')?.remove(); delete window.__aef_kook_rules_loaded;
        })()
        """;
}
