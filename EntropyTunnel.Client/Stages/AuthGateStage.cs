using System.Security.Cryptography;
using System.Text;
using EntropyTunnel.Client.Configuration;
using EntropyTunnel.Client.Pipeline;

namespace EntropyTunnel.Client.Stages;

/// <summary>
/// Stage 0 - Auth Gate.
/// When the client is started with --password, every request must carry a valid
/// _et_auth cookie before reaching the local service.
///
/// Flow:
///   POST /_tunnel_auth  ->  validate password, set cookie, redirect to /
///   Any request, no valid cookie  ->  serve the password page
///   Any request, valid cookie  ->  pass through
/// </summary>
public sealed class AuthGateStage : IPipelineStage
{
  private const string CookieName = "_et_auth";
  private const string AuthPath = "/_tunnel_auth";

  private readonly string? _token; // SHA-256 of the password, hex-encoded

  public AuthGateStage(TunnelSettings settings)
  {
    if (!string.IsNullOrEmpty(settings.TunnelPassword))
      _token = ComputeToken(settings.TunnelPassword);
  }

  public async Task ExecuteAsync(TunnelContext context, Func<Task> next, CancellationToken ct)
  {
    if (_token is null) { await next(); return; }

    var pathOnly = context.Path.Split('?')[0];

    if (context.Method == "POST" && pathOnly == AuthPath)
    {
      await HandleAuthPostAsync(context, ct);
      return;
    }

    if (HasValidCookie(context.RequestHeaders))
    {
      await next();
      return;
    }

    bool showError = context.Path.Contains("error=1");
    ServeAuthPage(context, showError);
  }

  // Helpers

  private bool HasValidCookie(Dictionary<string, string>? headers)
  {
    if (headers is null) return false;

    if (!headers.TryGetValue("Cookie", out var header) &&
        !headers.TryGetValue("cookie", out header))
      return false;

    foreach (var part in header.Split(';'))
    {
      var kv = part.Trim();
      var prefix = CookieName + "=";
      if (kv.StartsWith(prefix, StringComparison.Ordinal))
        return kv[prefix.Length..] == _token;
    }

    return false;
  }

  private async Task HandleAuthPostAsync(TunnelContext context, CancellationToken ct)
  {
    var body = string.Empty;
    if (context.RequestBody is not null)
    {
      using var reader = new StreamReader(context.RequestBody, Encoding.UTF8, leaveOpen: true);
      body = await reader.ReadToEndAsync(ct);
    }

    var password = ParseFormField(body, "password");

    if (ComputeToken(password) == _token)
    {
      Redirect(context, "/", [$"{CookieName}={_token}; Path=/; HttpOnly; SameSite=Lax"]);
    }
    else
    {
      Redirect(context, $"{AuthPath}?error=1");
    }
  }

  private static void Redirect(TunnelContext context, string location, string[]? setCookie = null)
  {
    context.StatusCode = 302;
    context.ContentType = "text/plain";
    context.ResponseHeaders = new Dictionary<string, string[]>
    {
      ["Location"] = [location]
    };
    if (setCookie is not null)
      context.ResponseHeaders["Set-Cookie"] = setCookie;
    context.ResponseStream = new MemoryStream();
    context.IsHandled = true;
  }

  private static void ServeAuthPage(TunnelContext context, bool showError)
  {
    var html = BuildAuthPage(showError);
    context.StatusCode = 200;
    context.ContentType = "text/html; charset=utf-8";
    context.ResponseStream = new MemoryStream(Encoding.UTF8.GetBytes(html));
    context.IsHandled = true;
  }

  private static string ParseFormField(string body, string field)
  {
    foreach (var part in body.Split('&'))
    {
      var eq = part.IndexOf('=');
      if (eq <= 0) continue;
      if (Uri.UnescapeDataString(part[..eq]) == field)
        return Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
    }
    return string.Empty;
  }

  private static string ComputeToken(string password)
  {
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(password));
    return Convert.ToHexString(hash).ToLowerInvariant();
  }

  // Auth page

  private static string BuildAuthPage(bool showError) => $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>Protected Tunnel</title>
          <style>
            *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
            body {
              font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
              background: #0f0f0f;
              color: #e0e0e0;
              min-height: 100vh;
              display: flex;
              align-items: center;
              justify-content: center;
            }
            .card {
              background: #161616;
              border: 1px solid #252525;
              border-radius: 14px;
              padding: 40px 36px;
              width: 340px;
              display: flex;
              flex-direction: column;
              gap: 22px;
            }
            .logo { font-size: 11px; font-weight: 700; letter-spacing: 0.12em; text-transform: uppercase; color: #444; }
            h1 { font-size: 17px; font-weight: 600; color: #f0f0f0; }
            .sub { font-size: 13px; color: #666; margin-top: 5px; }
            .error { font-size: 13px; color: #f87171; }
            input[type=password] {
              width: 100%;
              background: #0f0f0f;
              border: 1px solid #2e2e2e;
              border-radius: 8px;
              padding: 10px 13px;
              color: #f0f0f0;
              font-size: 14px;
              outline: none;
            }
            input[type=password]:focus { border-color: #484848; }
            button {
              width: 100%;
              background: #f0f0f0;
              color: #0f0f0f;
              border: none;
              border-radius: 8px;
              padding: 10px;
              font-size: 14px;
              font-weight: 600;
              cursor: pointer;
            }
            button:hover { background: #ddd; }
            .footer { text-align: center; }
            a { font-size: 12px; color: #444; text-decoration: none; }
            a:hover { color: #888; }
          </style>
        </head>
        <body>
          <div class="card">
            <span class="logo">EntropyTunnel</span>
            <div>
              <h1>Password required</h1>
              <p class="sub">This tunnel is password protected.</p>
            </div>
            {{(showError ? "<p class=\"error\">Incorrect password — try again.</p>" : "")}}
            <form id="f" method="POST" action="/_tunnel_auth">
              <input id="pw" type="password" name="password" placeholder="Enter password" autocomplete="off" autofocus />
            </form>
            <button type="button" onclick="submit()">Continue</button>
            <div class="footer">
              <a href="https://entropy-tunnel.xyz" target="_blank" rel="noopener">entropy-tunnel.xyz</a>
            </div>
          </div>
          <script>
            function submit() {
              document.getElementById('f').submit();
            }
            document.getElementById('pw').addEventListener('keydown', function(e) {
              if (e.key === 'Enter') submit();
            });
          </script>
        </body>
        </html>
        """;
}
