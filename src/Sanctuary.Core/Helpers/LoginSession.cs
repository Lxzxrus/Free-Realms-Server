namespace Sanctuary.Core.Helpers;

/// <summary>
/// The session a player signs in to the Login server with. The Web API makes it at sign-in
/// (<c>Guid.NewGuid().ToString("N")</c> in AuthEndpoints: 32 lowercase hex characters), the launcher passes it to the
/// game client unchanged, and Login looks it up once. Login refuses anything else before it asks the database, so a
/// client that hasn't signed in can't make Login query or log text of its own choosing (the go-public review's SEC-2:
/// a 64 KB "session", queried and logged twice, on every packet).
/// </summary>
public static class LoginSession
{
    public const int Length = 32;

    public static bool IsWellFormed(string? session)
    {
        if (session is not { Length: Length })
            return false;

        foreach (var c in session)
        {
            if (c is not ((>= '0' and <= '9') or (>= 'a' and <= 'f')))
                return false;
        }

        return true;
    }
}
