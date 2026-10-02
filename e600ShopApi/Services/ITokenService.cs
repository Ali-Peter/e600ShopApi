using e600ShopApi.Domain;

namespace e600ShopApi.Services;

public sealed record TokenResponse(string Token, DateTime ExpiresAt);

/// <summary>
/// Issues JWT access tokens. Google/email authentication flows will call into this
/// service once those features are implemented.
/// </summary>
public interface ITokenService
{
    TokenResponse GenerateToken(User user);
}
