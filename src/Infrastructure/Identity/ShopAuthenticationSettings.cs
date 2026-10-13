using System.Text;
using Microsoft.Extensions.Configuration;
using Retail360.Application.Identity;

namespace Retail360.Infrastructure.Identity;

public sealed class ShopAuthenticationSettings
{
    public const int MinimumSigningKeyBytes = 32;

    public ShopAuthenticationSettings(string issuer, string audience, string signingKey, int accessTokenMinutes)
    {
        Issuer = issuer;
        Audience = audience;
        SigningKey = signingKey;
        AccessTokenMinutes = accessTokenMinutes;
    }

    public string Issuer { get; }

    public string Audience { get; }

    public string SigningKey { get; }

    public int AccessTokenMinutes { get; }

    public static ShopAuthenticationSettings Read(IConfiguration configuration)
    {
        var issuer = configuration["Authentication:Issuer"];
        var audience = configuration["Authentication:Audience"];
        var signingKey = configuration["Authentication:SigningKey"];
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
        {
            throw new InvalidOperationException("Authentication issuer and audience are required.");
        }

        if (string.IsNullOrEmpty(signingKey) || Encoding.UTF8.GetByteCount(signingKey) < MinimumSigningKeyBytes)
        {
            throw new InvalidOperationException(
                "Authentication:SigningKey is missing or shorter than 32 bytes.");
        }

        var minutesText = configuration["Authentication:AccessTokenMinutes"];
        var minutes = 15;
        if (!string.IsNullOrWhiteSpace(minutesText)
            && (!int.TryParse(minutesText, out minutes) || minutes < 1 || minutes > 60))
        {
            throw new InvalidOperationException("Authentication:AccessTokenMinutes must be from 1 to 60.");
        }

        return new ShopAuthenticationSettings(issuer.Trim(), audience.Trim(), signingKey, minutes);
    }

    public static ShopLockoutPolicy ReadLockout(IConfiguration configuration)
    {
        var attempts = ReadBoundedInt(
            configuration["Authentication:MaxFailedAttempts"],
            ShopLockoutPolicy.Default.MaxFailedAttempts,
            1,
            20,
            "Authentication:MaxFailedAttempts");
        var minutes = ReadBoundedInt(
            configuration["Authentication:LockoutMinutes"],
            (int)ShopLockoutPolicy.Default.Duration.TotalMinutes,
            1,
            1440,
            "Authentication:LockoutMinutes");
        return new ShopLockoutPolicy(attempts, TimeSpan.FromMinutes(minutes));
    }

    private static int ReadBoundedInt(string? text, int fallback, int minimum, int maximum, string name)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        if (!int.TryParse(text, out var value) || value < minimum || value > maximum)
        {
            throw new InvalidOperationException($"{name} must be from {minimum} to {maximum}.");
        }

        return value;
    }
}
