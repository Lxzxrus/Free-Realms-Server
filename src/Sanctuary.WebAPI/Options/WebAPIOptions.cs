using System;
using System.Collections.Generic;

namespace Sanctuary.WebAPI.Options;

public class WebAPIOptions
{
    public const string Section = "WebAPI";

    /// <summary>
    /// Arguments handed to the client on login. Must not contain <c>Portrait:UploadUrl</c>;
    /// that argument is built per login from <see cref="PortraitUploadUrl"/>.
    /// </summary>
    public string? LaunchArguments { get; set; }

    public bool? MemberByDefault { get; set; }

    /// <summary>
    /// Public base URL of the portrait endpoint, for example <c>https://play.example.com/image</c>.
    /// Each login appends a signed token to it. Leave it empty to turn portrait uploads off.
    /// </summary>
    public string? PortraitUploadUrl { get; set; }

    /// <summary>How long a portrait upload token from one login stays valid.</summary>
    public TimeSpan PortraitUploadTokenLifetime { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Largest portrait upload request accepted, in bytes.</summary>
    public long PortraitMaxRequestBytes { get; set; } = 1024 * 1024;

    /// <summary>Where portraits are stored, one folder per character guid.</summary>
    public string ImagesDirectory { get; set; } = "Images";

    /// <summary>
    /// Proxies (addresses or CIDR ranges) whose <c>X-Forwarded-For</c> is trusted. Empty means loopback only,
    /// which fits a reverse proxy on the same machine.
    /// </summary>
    public List<string> TrustedProxies { get; set; } = [];

    /// <summary>The Login server that <c>/status</c> asks, host and UDP port. In Docker Compose, <c>sanctuary.login:20042</c>.</summary>
    public string StatusLoginServer { get; set; } = "127.0.0.1:20042";

    /// <summary>How long <c>/status</c> waits for the Login server before reporting it offline.</summary>
    public TimeSpan StatusTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>How long one answer from the Login server is reused, so the status page can't flood it.</summary>
    public TimeSpan StatusCacheDuration { get; set; } = TimeSpan.FromSeconds(10);

    public RateLimitOptions RateLimits { get; set; } = new();

    public LoginLockoutOptions LoginLockout { get; set; } = new();
}

/// <summary>Per-client-address request limits. Behind a proxy the address comes from <c>X-Forwarded-For</c>.</summary>
public class RateLimitOptions
{
    public int LoginPerMinute { get; set; } = 10;

    public int RegisterPerHour { get; set; } = 5;

    public int ImagePerMinute { get; set; } = 6;

    public int StatusPerMinute { get; set; } = 30;
}

/// <summary>
/// Temporary lockouts after failed logins. Every lockout expires on its own.
/// </summary>
public class LoginLockoutOptions
{
    /// <summary>Failures from one address on one account before that address is locked out of it.</summary>
    public int FailuresPerAddress { get; set; } = 5;

    /// <summary>First lockout for an address. Each further failure doubles it, up to <see cref="MaxLockout"/>.</summary>
    public TimeSpan BaseLockout { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan MaxLockout { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>An address's failure count starts again from zero after this long without a failure.</summary>
    public TimeSpan ForgetFailuresAfter { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Failures from all addresses together, within <see cref="AccountWindow"/>, before the account is locked for
    /// <see cref="AccountWindow"/>. Addresses that have logged into the account before are not affected.
    /// </summary>
    public int FailuresPerAccount { get; set; } = 20;

    public TimeSpan AccountWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>How long an address that logged in successfully stays exempt from the account-wide lockout.</summary>
    public TimeSpan TrustedAddressLifetime { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Most accounts and account/address pairs tracked at once, to bound memory.</summary>
    public int MaxTrackedEntries { get; set; } = 100_000;
}
