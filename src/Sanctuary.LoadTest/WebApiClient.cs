using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sanctuary.LoadTest;

/// <summary>
/// The same two calls run_client.py makes: log in, and register first if the account doesn't exist yet.
/// Registering through WebAPI is the account seeding; nothing is written to the database directly.
/// </summary>
public sealed class WebApiClient(HttpClient httpClient)
{
    private sealed record Credentials(string Username, string Password);
    private sealed record LoginResponse(string SessionId, string? LaunchArguments);

    public async Task<string> GetSessionAsync(string username, string password, CancellationToken cancellationToken)
    {
        var credentials = new Credentials(username, password);

        using (var login = await httpClient.PostAsJsonAsync("/login", credentials, cancellationToken))
        {
            if (login.IsSuccessStatusCode)
                return await ReadSessionAsync(login, cancellationToken);

            if (login.StatusCode != HttpStatusCode.Unauthorized)
                throw new InvalidOperationException($"WebAPI /login for {username} returned {(int)login.StatusCode}.");
        }

        // 401 is "no such user" or "wrong password". Registering tells them apart: 409 means the account exists.
        using (var register = await httpClient.PostAsJsonAsync("/register", credentials, cancellationToken))
        {
            if (register.StatusCode == HttpStatusCode.Conflict)
                throw new InvalidOperationException($"{username} exists with a different password.");

            if (!register.IsSuccessStatusCode)
                throw new InvalidOperationException($"WebAPI /register for {username} returned {(int)register.StatusCode}.");
        }

        using var retry = await httpClient.PostAsJsonAsync("/login", credentials, cancellationToken);

        if (!retry.IsSuccessStatusCode)
            throw new InvalidOperationException($"WebAPI /login for {username} returned {(int)retry.StatusCode} after registering.");

        return await ReadSessionAsync(retry, cancellationToken);
    }

    private static async Task<string> ReadSessionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);

        if (string.IsNullOrEmpty(body?.SessionId))
            throw new InvalidOperationException("WebAPI /login returned no session id.");

        return body.SessionId;
    }
}
