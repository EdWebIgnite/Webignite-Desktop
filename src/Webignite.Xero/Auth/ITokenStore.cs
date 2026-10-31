namespace Webignite.Xero.Auth;

/// <summary>Keeps Xero's refresh token between runs. The access token only ever lives in memory.</summary>
public interface ITokenStore
{
    string? ReadRefreshToken();
    void WriteRefreshToken(string refreshToken);
    void Clear();
}

public sealed class MemoryTokenStore : ITokenStore
{
    private string? token;
    public string? ReadRefreshToken() => token;
    public void WriteRefreshToken(string refreshToken) => token = refreshToken;
    public void Clear() => token = null;
}
