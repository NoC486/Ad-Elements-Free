using AdElementsFree.Rules;
using System.Text.Json;

namespace AdElementsFree.Providers.KOOK;

public static class KookRules
{
    public const string StyleId = "ad-elements-free-kook";
    public static string Build(string pageUrl)
    {
        var css = JsonSerializer.Serialize(RuleFiles.Read("KOOK"));
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
          if (apply()) return true;
          // At document creation the head may not exist yet. Queue once, without a timer.
          if (document.readyState === 'loading') {
            if (window.__aef_kook_pending) document.removeEventListener('DOMContentLoaded', window.__aef_kook_pending);
            window.__aef_kook_pending = () => { delete window.__aef_kook_pending; apply(); };
            document.addEventListener('DOMContentLoaded', window.__aef_kook_pending, {once:true});
            return true;
          }
          return false;
        })()
        """;
    }
    public static string Remove(string pageUrl) => $$"""
        (() => {
          const expected = new URL({{JsonSerializer.Serialize(pageUrl)}});
          if (location.protocol !== expected.protocol || location.host !== expected.host || location.pathname !== expected.pathname) return;
          if (window.__aef_kook_pending) document.removeEventListener('DOMContentLoaded', window.__aef_kook_pending);
          delete window.__aef_kook_pending;
          document.getElementById('{{StyleId}}')?.remove(); delete window.__aef_kook_rules_loaded;
        })()
        """;
}
