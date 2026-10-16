namespace Sanctuary.Game;

/// <summary>
/// Evergrove has no paid membership, so every account is a member: nothing the client locks behind membership (items,
/// activities, jobs, house lots) is out of reach, and everyone pays the member price. The database's
/// <c>Users.IsMember</c> and <c>Characters.MembershipStatus</c> are kept, but not read.
/// </summary>
public static class Membership
{
    /// <summary>The character membership status the client reads as "member", which character creation stored for members.</summary>
    public const int MemberStatus = 2;
}
