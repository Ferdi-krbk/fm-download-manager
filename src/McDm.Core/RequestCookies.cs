namespace McDm.Core;

/// <summary>Cookies are scoped to the destination, never to the whole media job.</summary>
public static class RequestCookies
{
    public static string? HeaderFor(DownloadRequest request, Uri destination)
    {
        if (request.Cookies is { Count: > 0 })
        {
            var cookies = request.Cookies.Where(c => Matches(c, destination));
            string header = string.Join("; ", cookies.OrderByDescending(c => c.Path?.Length ?? 1)
                .Select(c => $"{c.Name}={c.Value}"));
            return header.Length == 0 ? null : header;
        }
        // Legacy raw headers have no domain metadata; only use them on the original origin.
        if (Uri.TryCreate(request.Url, UriKind.Absolute, out var source)
            && source.Scheme == destination.Scheme && source.Host.Equals(destination.Host, StringComparison.OrdinalIgnoreCase)
            && source.Port == destination.Port && !string.IsNullOrWhiteSpace(request.CookieHeader)
            && !request.CookieHeader.Contains('\r') && !request.CookieHeader.Contains('\n'))
            return request.CookieHeader;
        return null;
    }

    public static bool Matches(BrowserCookie cookie, Uri destination)
    {
        string domain = cookie.Domain ?? "";
        bool hostMatches = domain.StartsWith('.')
            ? destination.Host.Equals(domain[1..], StringComparison.OrdinalIgnoreCase)
                || destination.Host.EndsWith(domain, StringComparison.OrdinalIgnoreCase)
            : destination.Host.Equals(domain, StringComparison.OrdinalIgnoreCase);
        string path = string.IsNullOrEmpty(cookie.Path) ? "/" : cookie.Path;
        string target = destination.AbsolutePath;
        return hostMatches && (!cookie.Secure || destination.Scheme == "https")
            && (!cookie.ExpirationDate.HasValue || cookie.ExpirationDate <= 0
                || cookie.ExpirationDate > DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            && (target == path || target.StartsWith(path, StringComparison.Ordinal)
                && (path.EndsWith('/') || target.Length > path.Length && target[path.Length] == '/'))
            && !string.IsNullOrEmpty(cookie.Name) && !cookie.Name.Any(c => c is '\r' or '\n' or ';' or '=')
            && !cookie.Value.Any(c => c is '\r' or '\n' or ';');
    }

    public static HttpClient CreateClient(DownloadRequest request, TimeSpan timeout)
    {
        var jar = new System.Net.CookieContainer();
        if (request.Cookies is { Count: > 0 })
        {
            foreach (var c in request.Cookies)
            {
                if (string.IsNullOrWhiteSpace(c.Domain)) continue;
                try
                {
                    var cookie = new System.Net.Cookie(c.Name, c.Value, c.Path ?? "/") { Secure = c.Secure, HttpOnly = c.HttpOnly };
                    if (c.ExpirationDate is > 0) cookie.Expires = DateTimeOffset.FromUnixTimeSeconds((long)c.ExpirationDate.Value).UtcDateTime;
                    if (c.Domain.StartsWith('.'))
                    {
                        cookie.Domain = c.Domain;
                        jar.Add(cookie);
                    }
                    else
                    {
                        // Adding with a URI preserves host-only semantics across redirects.
                        jar.Add(new UriBuilder(c.Secure ? "https" : "http", c.Domain).Uri, cookie);
                    }
                }
                catch (Exception ex) when (ex is System.Net.CookieException or ArgumentException) { }
            }
        }
        else if (Uri.TryCreate(request.Url, UriKind.Absolute, out var origin) && !string.IsNullOrWhiteSpace(request.CookieHeader))
        {
            foreach (var pair in request.CookieHeader.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                try { jar.Add(origin, new System.Net.Cookie(pair[..eq], pair[(eq + 1)..], "/") { Secure = origin.Scheme == "https" }); }
                catch (System.Net.CookieException) { }
            }
        }
        var client = new HttpClient(new HttpClientHandler { CookieContainer = jar, UseCookies = true }) { Timeout = timeout };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", request.UserAgent ?? "FMDownloadManager/1.0.0");
        if (!string.IsNullOrEmpty(request.Referer)) client.DefaultRequestHeaders.TryAddWithoutValidation("Referer", request.Referer);
        return client;
    }
}
