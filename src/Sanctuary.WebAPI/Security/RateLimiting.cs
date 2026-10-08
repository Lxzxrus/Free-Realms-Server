using System;
using System.Globalization;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

using Sanctuary.WebAPI.Options;

namespace Sanctuary.WebAPI.Security;

public static class RateLimiting
{
    public const string LoginPolicy = "login";
    public const string RegisterPolicy = "register";
    public const string ImagePolicy = "image";
    public const string StatusPolicy = "status";

    /// <summary>
    /// Per-address fixed windows for each endpoint. Rejections are 429 with a <c>Retry-After</c> in seconds.
    /// </summary>
    public static IServiceCollection AddWebAPIRateLimiting(this IServiceCollection services, RateLimitOptions limits)
    {
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds(retryAfter);

                return ValueTask.CompletedTask;
            };

            options.AddPolicy(LoginPolicy, context => PerAddress(context, limits.LoginPerMinute, TimeSpan.FromMinutes(1)));
            options.AddPolicy(RegisterPolicy, context => PerAddress(context, limits.RegisterPerHour, TimeSpan.FromHours(1)));
            options.AddPolicy(ImagePolicy, context => PerAddress(context, limits.ImagePerMinute, TimeSpan.FromMinutes(1)));
            options.AddPolicy(StatusPolicy, context => PerAddress(context, limits.StatusPerMinute, TimeSpan.FromMinutes(1)));
        });
    }

    public static string RetryAfterSeconds(TimeSpan retryAfter)
    {
        return Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
    }

    private static RateLimitPartition<string> PerAddress(HttpContext context, int permitLimit, TimeSpan window)
    {
        return RateLimitPartition.GetFixedWindowLimiter(ClientAddress.GetKey(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }
}
