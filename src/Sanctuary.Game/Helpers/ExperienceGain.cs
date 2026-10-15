namespace Sanctuary.Game.Helpers;

/// <summary>
/// Stars added to one job: how many, and the job's level before and after, with its stars into the new level.
/// </summary>
public readonly record struct ExperienceGain(int ProfileId, int Amount, int OldLevel, int Level, int LevelXp)
{
    public bool LeveledUp => Level > OldLevel;
}
