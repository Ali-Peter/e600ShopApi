using System.IdentityModel.Tokens.Jwt;
using e600ShopApi.Domain;
using e600ShopApi.Services;
using Microsoft.Extensions.Configuration;

namespace e600ShopApi.Tests;

public class TokenServiceTests
{
    // Test-only signing key (in-memory configuration, never used outside unit tests).
    private const string TestSecret = "unit-test-signing-key-for-e600shop-0123456789abcdef";

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static User CreateSampleUser() =>
        new()
        {
            Id = Guid.NewGuid(),
            Email = "ada@example.com",
            FirstName = "Ada",
            LastName = "Obi",
        };

    [Fact]
    public void GenerateToken_Produces_Signed_Jwt_With_User_Claims()
    {
        var configuration = BuildConfiguration(new()
        {
            ["Jwt:Secret"] = TestSecret,
            ["Jwt:Issuer"] = "e600ShopApi",
            ["Jwt:Audience"] = "e600Shop",
            ["Jwt:ExpirationMinutes"] = "60",
        });
        var service = new TokenService(configuration);
        var user = CreateSampleUser();

        var result = service.GenerateToken(user);

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.True(result.ExpiresAt > DateTime.UtcNow);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Equal(user.Id.ToString(), jwt.Claims.Single(claim => claim.Type == "sub").Value);
        Assert.Equal(user.Email, jwt.Claims.Single(claim => claim.Type == "email").Value);
        Assert.Equal("e600ShopApi", jwt.Issuer);
        Assert.Equal("e600Shop", jwt.Audiences.Single());
    }

    [Fact]
    public void GenerateToken_Throws_When_Secret_Is_Missing()
    {
        var configuration = BuildConfiguration(new()
        {
            ["Jwt:Issuer"] = "e600ShopApi",
        });
        var service = new TokenService(configuration);

        var exception = Assert.Throws<InvalidOperationException>(
            () => service.GenerateToken(CreateSampleUser()));

        Assert.Contains("Jwt:Secret", exception.Message);
    }
}
