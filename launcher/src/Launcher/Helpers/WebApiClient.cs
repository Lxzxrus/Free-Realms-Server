using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Launcher.Models;

namespace Launcher.Helpers;

public enum WebApiStatus
{
    Ok,
    Invalid,
    WrongCredentials,
    Banned,
    NameTaken,
    TooManyAttempts,
    ServerDown,
    Insecure,
    Unexpected
}

public sealed class WebApiResult
{
    public required WebApiStatus Status { get; init; }

    /// <summary>Set on a successful login.</summary>
    public LoginResponse? Login { get; init; }

    /// <summary>From <c>Retry-After</c> on a 429.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Field errors from a 400.</summary>
    public IReadOnlyList<string> Errors { get; init; } = [];

    /// <summary>The HTTP status, when there was an answer.</summary>
    public HttpStatusCode? HttpStatus { get; init; }
}

/// <summary>
/// Calls WebAPI's <c>/register</c> and <c>/login</c> as described in docs/webapi.md ("Contract for the launcher").
/// Nothing here retries: WebAPI counts every failed login towards a lockout.
/// </summary>
public static class WebApiClient
{
    public static Task<WebApiResult> LoginAsync(HttpClient httpClient, string webApiUrl, string username, string password, CancellationToken cancellationToken = default)
        => PostAsync(httpClient, webApiUrl, "login", new LoginRequest { Username = username, Password = password }, isLogin: true, cancellationToken);

    public static Task<WebApiResult> RegisterAsync(HttpClient httpClient, string webApiUrl, string username, string password, CancellationToken cancellationToken = default)
        => PostAsync(httpClient, webApiUrl, "register", new RegisterRequest { Username = username, Password = password }, isLogin: false, cancellationToken);

    private static async Task<WebApiResult> PostAsync<T>(HttpClient httpClient, string webApiUrl, string path, T body, bool isLogin, CancellationToken cancellationToken)
    {
        try
        {
            var uri = new Uri(UriHelper.JoinUriPaths(webApiUrl, path));

            using var response = await httpClient.PostAsJsonAsync(uri, body, cancellationToken);

            return await ReadAsync(response, isLogin, DateTimeOffset.UtcNow, cancellationToken);
        }
        catch (InsecureTransportException)
        {
            return new WebApiResult { Status = WebApiStatus.Insecure };
        }
        catch (HttpRequestException)
        {
            return new WebApiResult { Status = WebApiStatus.ServerDown };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient's timeout.
            return new WebApiResult { Status = WebApiStatus.ServerDown };
        }
    }

    internal static async Task<WebApiResult> ReadAsync(HttpResponseMessage response, bool isLogin, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var httpStatus = response.StatusCode;

        switch (httpStatus)
        {
            case HttpStatusCode.OK when isLogin:
                LoginResponse? login;

                try
                {
                    login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
                }
                catch (Exception ex) when (ex is JsonException or NotSupportedException)
                {
                    login = null;
                }

                if (login is null || string.IsNullOrEmpty(login.SessionId))
                    return new WebApiResult { Status = WebApiStatus.Unexpected, HttpStatus = httpStatus };

                return new WebApiResult { Status = WebApiStatus.Ok, Login = login, HttpStatus = httpStatus };

            case HttpStatusCode.OK:
                return new WebApiResult { Status = WebApiStatus.Ok, HttpStatus = httpStatus };

            case HttpStatusCode.BadRequest:
            case HttpStatusCode.RequestEntityTooLarge:
                return new WebApiResult
                {
                    Status = WebApiStatus.Invalid,
                    Errors = await ReadProblemErrorsAsync(response, cancellationToken),
                    HttpStatus = httpStatus
                };

            case HttpStatusCode.Unauthorized when isLogin:
                return new WebApiResult { Status = WebApiStatus.WrongCredentials, HttpStatus = httpStatus };

            case HttpStatusCode.Forbidden when isLogin:
                return new WebApiResult { Status = WebApiStatus.Banned, HttpStatus = httpStatus };

            case HttpStatusCode.Conflict when !isLogin:
                return new WebApiResult { Status = WebApiStatus.NameTaken, HttpStatus = httpStatus };

            case HttpStatusCode.TooManyRequests:
                return new WebApiResult
                {
                    Status = WebApiStatus.TooManyAttempts,
                    RetryAfter = GetRetryAfter(response, now),
                    HttpStatus = httpStatus
                };

            case >= HttpStatusCode.InternalServerError:
                return new WebApiResult { Status = WebApiStatus.ServerDown, HttpStatus = httpStatus };

            default:
                return new WebApiResult { Status = WebApiStatus.Unexpected, HttpStatus = httpStatus };
        }
    }

    internal static TimeSpan? GetRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter?.Delta is { } delta)
            return delta;

        if (retryAfter?.Date is { } date)
            return date > now ? date - now : TimeSpan.Zero;

        return null;
    }

    /// <summary>
    /// Reads the field errors of an ASP.NET validation problem (<c>{"errors": {"Username": ["..."]}}</c>).
    /// </summary>
    private static async Task<IReadOnlyList<string>> ReadProblemErrorsAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);

            if (!document.RootElement.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Object)
                return [];

            return errors.EnumerateObject()
                .Where(x => x.Value.ValueKind == JsonValueKind.Array)
                .SelectMany(x => x.Value.EnumerateArray())
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString()!)
                .Distinct()
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// "45 seconds", "1 minute", "15 minutes". Rounds up, so the player never comes back too early.
    /// </summary>
    public static string FormatWait(TimeSpan? wait)
    {
        if (wait is not { } value || value <= TimeSpan.Zero)
            return "a few minutes";

        if (value < TimeSpan.FromMinutes(1))
        {
            var seconds = (int)Math.Ceiling(value.TotalSeconds);
            return seconds == 1 ? "1 second" : $"{seconds} seconds";
        }

        var minutes = (int)Math.Ceiling(value.TotalMinutes);
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }
}
