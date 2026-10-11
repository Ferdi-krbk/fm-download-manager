using System.Security.Cryptography;
using System.Text;

namespace McDm.Core;

/// <summary>
/// Pairing token for the browser bridge: 256 random bits generated on first run and stored per user
/// (%LOCALAPPDATA%\MC Download Manager\bridge.token). The extension must send it as X-MCDM-Token.
/// </summary>
public sealed class TokenStore
{
    public string Token { get; private set; }
    private readonly string _path;

    public TokenStore(string? path = null)
    {
        _path = path ?? AppPaths.TokenFile();
        Token = LoadOrCreate();
    }

    private string LoadOrCreate()
    {
        try
        {
            if (File.Exists(_path))
            {
                var t = File.ReadAllText(_path).Trim();
                if (IsWellFormed(t)) return t;
            }
        }
        catch { }
        return Regenerate(persistOnly: true);
    }

    /// <summary>Creates a new token (invalidates every paired browser).</summary>
    public string Regenerate() => Regenerate(persistOnly: false);

    private string Regenerate(bool persistOnly)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, token);
        Token = token;
        return token;
    }

    private static bool IsWellFormed(string t) => t.Length == 64 && t.All(Uri.IsHexDigit);

    /// <summary>Constant-time comparison.</summary>
    public bool Matches(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate)) return false;
        var a = Encoding.UTF8.GetBytes(Token);
        var b = Encoding.UTF8.GetBytes(candidate);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
