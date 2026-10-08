using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using Sanctuary.WebAPI.Security;
using Sanctuary.WebAPI.Status;

namespace Sanctuary.WebAPI.Endpoints;

/// <summary>
/// Public server status: <c>/status.json</c> for monitors (UptimeRobot looks for <c>"status":"online"</c>), and
/// <c>/status</c>, a page that shows it to people. Both say only what the launcher already shows anyone.
/// </summary>
public static class StatusEndpoints
{
    public static void MapStatusEndpoints(this WebApplication app)
    {
        app.MapGet("/status.json", StatusJsonAsync).RequireRateLimiting(RateLimiting.StatusPolicy);
        app.MapGet("/status", StatusPage).RequireRateLimiting(RateLimiting.StatusPolicy);
    }

    private static async Task<IResult> StatusJsonAsync(HttpContext context, ServerStatusProbe probe, CancellationToken cancellationToken)
    {
        var status = await probe.GetAsync(cancellationToken);

        context.Response.Headers.CacheControl = "no-store";

        return Results.Json(new
        {
            status = status.Status,
            online = status.Online,
            locked = status.Locked,
            players = status.Players,
            checkedAt = status.CheckedAt
        });
    }

    private static IResult StatusPage(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-cache";
        context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; connect-src 'self'";

        return Results.Content(Page, "text/html; charset=utf-8");
    }

    private const string Page = """
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>Evergrove Status</title>
        <style>
          :root { --bg: #f4f7f2; --card: #ffffff; --text: #1d2a1f; --muted: #5b6b5e; --green: #23a55a; --amber: #c98a00; --red: #e03e3e; }
          @media (prefers-color-scheme: dark) { :root { --bg: #141a15; --card: #1f2820; --text: #e8efe8; --muted: #9aab9c; } }
          body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: var(--bg); color: var(--text);
                 font: 16px/1.5 system-ui, -apple-system, "Segoe UI", sans-serif; }
          main { background: var(--card); border-radius: 16px; padding: 32px 36px; width: min(420px, calc(100vw - 32px));
                 box-sizing: border-box; box-shadow: 0 2px 16px rgb(0 0 0 / 0.08); }
          h1 { margin: 0 0 20px; font-size: 1.25rem; }
          .state { display: flex; align-items: center; gap: 12px; font-size: 1.5rem; font-weight: 600; }
          .dot { width: 16px; height: 16px; border-radius: 50%; background: var(--muted); flex: none; }
          .online .dot { background: var(--green); box-shadow: 0 0 0 6px rgb(35 165 90 / 0.18); }
          .locked .dot { background: var(--amber); }
          .offline .dot { background: var(--red); }
          dl { display: grid; grid-template-columns: auto 1fr; gap: 6px 16px; margin: 20px 0 0; color: var(--muted); }
          dd { margin: 0; color: var(--text); text-align: right; }
          footer { margin-top: 20px; font-size: 0.85rem; color: var(--muted); }
        </style>
        </head>
        <body>
        <main>
          <h1>Free Realms Evergrove</h1>
          <div id="state" class="state"><span class="dot"></span><span id="label">Checking…</span></div>
          <dl>
            <dt>Players online</dt><dd id="players">–</dd>
            <dt>Last checked</dt><dd id="checked">–</dd>
          </dl>
          <footer>Updates every 30 seconds.</footer>
        </main>
        <script>
          const labels = { online: "Online", locked: "Maintenance", offline: "Offline" };
          async function refresh() {
            const state = document.getElementById("state");
            try {
              const response = await fetch("status.json", { cache: "no-store" });
              const status = await response.json();
              state.className = "state " + status.status;
              document.getElementById("label").textContent = labels[status.status] ?? status.status;
              document.getElementById("players").textContent = status.online ? String(status.players) : "–";
              document.getElementById("checked").textContent = new Date(status.checkedAt).toLocaleTimeString();
            } catch {
              state.className = "state offline";
              document.getElementById("label").textContent = "Can't reach the server";
              document.getElementById("players").textContent = "–";
            }
          }
          refresh();
          setInterval(refresh, 30000);
        </script>
        </body>
        </html>
        """;
}
