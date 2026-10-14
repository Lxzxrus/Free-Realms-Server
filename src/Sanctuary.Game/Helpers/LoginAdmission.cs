namespace Sanctuary.Game.Helpers;

/// <summary>Why the Gateway turns a character login away, on top of the ticket check.</summary>
public enum LoginRefusal
{
    None,

    /// <summary>The connection already has a player: a second login would leave the first one in the world as a
    /// ghost that is never saved or logged out.</summary>
    ConnectionAlreadyLoggedIn,

    /// <summary>Another connection on this Gateway already plays the character. The Login server refuses that too,
    /// but a second ticket can be fetched before it hears about the first login.</summary>
    CharacterAlreadyOnline
}

/// <summary>
/// One player per connection and one connection per character. Two sessions of one character each keep their own
/// inventory, quest and coin state, so whichever finishes a quest second would pay its reward again (task 30).
/// </summary>
public static class LoginAdmission
{
    public static LoginRefusal Check(bool connectionHasPlayer, bool characterOnline)
    {
        if (connectionHasPlayer)
            return LoginRefusal.ConnectionAlreadyLoggedIn;

        if (characterOnline)
            return LoginRefusal.CharacterAlreadyOnline;

        return LoginRefusal.None;
    }
}
