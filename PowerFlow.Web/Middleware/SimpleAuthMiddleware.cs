using System.Security.Cryptography;
using System.Text;

namespace PowerFlow.Web.Middleware;

// Single-shared-password gate. Drop in front of the rest of the pipeline:
// every request that doesn't carry a valid auth cookie is redirected to the
// login page, including static assets and the Blazor SignalR endpoint.
//
// Configure via the PF_PASSWORD environment variable (Fly secret in prod,
// user-secrets or appsettings.Development.json locally). If unset, the
// middleware is a no-op — useful for local dev.
public sealed class SimpleAuthMiddleware
{
    private const string CookieName = "pf_auth";
    private const string LoginPath  = "/pf-login";

    private readonly RequestDelegate _next;
    private readonly string _password;
    private readonly string _cookieValue;

    public SimpleAuthMiddleware(RequestDelegate next, IConfiguration config)
    {
        _next = next;
        _password = config["PF_PASSWORD"]
                    ?? Environment.GetEnvironmentVariable("PF_PASSWORD")
                    ?? "";
        // Cookie value is a hash of the password so it survives app restarts
        // (no in-memory session store needed) and rotates automatically when
        // the password changes — all old cookies become invalid.
        _cookieValue = string.IsNullOrEmpty(_password) ? "" : Hash(_password);
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        // No password configured → fully open. Lets the dev loop work without
        // setting any env vars.
        if (string.IsNullOrEmpty(_password))
        {
            await _next(ctx);
            return;
        }

        var path = ctx.Request.Path.Value ?? "";

        // Liveness probe must stay reachable so Fly's health check passes
        // even before the user authenticates. Match Program.cs's MapGet.
        if (path.Equals("/healthz", StringComparison.OrdinalIgnoreCase))
        {
            await _next(ctx);
            return;
        }

        if (path.Equals(LoginPath, StringComparison.OrdinalIgnoreCase))
        {
            await HandleLogin(ctx);
            return;
        }

        if (ctx.Request.Cookies.TryGetValue(CookieName, out var v) && v == _cookieValue)
        {
            await _next(ctx);
            return;
        }

        // Anything else → bounce to login. Browsers follow this; SignalR/WebSocket
        // attempts will fail, but an unauthenticated client never gets the page
        // that would initiate them in the first place.
        ctx.Response.Redirect(LoginPath);
    }

    private async Task HandleLogin(HttpContext ctx)
    {
        if (ctx.Request.Method == "POST")
        {
            var form = await ctx.Request.ReadFormAsync();
            var pw = form["password"].ToString();

            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(pw),
                    Encoding.UTF8.GetBytes(_password)))
            {
                ctx.Response.Cookies.Append(CookieName, _cookieValue, new CookieOptions
                {
                    HttpOnly = true,
                    Secure   = ctx.Request.IsHttps,
                    SameSite = SameSiteMode.Lax,
                    Expires  = DateTimeOffset.UtcNow.AddDays(30),
                    Path     = "/"
                });
                ctx.Response.Redirect("/");
                return;
            }

            await WriteLoginPage(ctx, "Wrong password.");
            return;
        }

        await WriteLoginPage(ctx, null);
    }

    private static async Task WriteLoginPage(HttpContext ctx, string? error)
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.StatusCode  = error == null ? 200 : 401;

        var errBlock = error == null
            ? ""
            : $"<p class=\"err\">{System.Net.WebUtility.HtmlEncode(error)}</p>";

        var html = $$"""
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width,initial-scale=1">
          <title>PowerFlow — Sign in</title>
          <style>
            :root { color-scheme: dark; }
            html, body {
              height: 100%; margin: 0;
              font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
              background: radial-gradient(ellipse at top, #1a2942 0%, #0b1220 60%, #050810 100%);
              color: #e2e8f0;
            }
            body {
              display: flex; align-items: center; justify-content: center;
            }
            form {
              width: min(360px, calc(100% - 2rem));
              background: rgba(22, 32, 53, 0.85);
              border: 1px solid rgba(255,255,255,0.08);
              border-radius: 12px;
              padding: 1.75rem 1.5rem 1.5rem;
              box-shadow: 0 20px 60px -10px rgba(0,0,0,0.6);
              backdrop-filter: blur(8px);
            }
            h1 {
              margin: 0 0 0.25rem; font-size: 1.15rem; font-weight: 700;
              letter-spacing: -0.018em;
            }
            .sub {
              margin: 0 0 1.25rem; font-size: 0.78rem; color: #94a3b8;
            }
            label {
              display: block; font-size: 0.72rem; color: #94a3b8;
              margin-bottom: 0.4rem; letter-spacing: 0.02em;
              text-transform: uppercase; font-weight: 600;
            }
            input[type=password] {
              width: 100%; box-sizing: border-box;
              padding: 0.6rem 0.75rem; font-size: 0.92rem;
              background: rgba(15, 23, 42, 0.7);
              color: #e2e8f0;
              border: 1px solid rgba(255,255,255,0.1);
              border-radius: 7px;
              outline: none;
              transition: border-color 0.12s, box-shadow 0.12s;
            }
            input[type=password]:focus {
              border-color: #3b82f6;
              box-shadow: 0 0 0 3px rgba(59,130,246,0.18);
            }
            button {
              margin-top: 1rem; width: 100%;
              padding: 0.6rem 0.75rem; font-size: 0.9rem; font-weight: 600;
              background: #2563eb; color: white;
              border: 0; border-radius: 7px; cursor: pointer;
              transition: background 0.12s;
            }
            button:hover { background: #1d4ed8; }
            .err {
              margin: 0.75rem 0 0; padding: 0.5rem 0.7rem;
              font-size: 0.8rem;
              background: rgba(239, 68, 68, 0.12);
              border: 1px solid rgba(239, 68, 68, 0.35);
              color: #fca5a5;
              border-radius: 6px;
            }
          </style>
        </head>
        <body>
          <form method="post" action="{{LoginPath}}">
            <h1>PowerFlow</h1>
            <p class="sub">Enter the access password to continue.</p>
            <label for="pw">Password</label>
            <input id="pw" type="password" name="password" autofocus required autocomplete="current-password">
            {{errBlock}}
            <button type="submit">Sign in</button>
          </form>
        </body>
        </html>
        """;

        await ctx.Response.WriteAsync(html);
    }

    private static string Hash(string s)
    {
        // Salted SHA-256 → hex. The salt isn't a real secret; it just keeps
        // the cookie value from being a bare password hash that an attacker
        // could rainbow-table. Adequate for a single-shared-password gate.
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s + "::pf-cookie::v1"));
        return Convert.ToHexString(bytes);
    }
}
