using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Minigames;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class FlashMinigameSessionTests
{
    [TestMethod]
    public void MessageNames_AreLoggedOnce_AndOnlyUpToTheLimit()
    {
        var session = new FlashMinigameSession(1, new FlashMinigameDefinition(1113, 375, 8, "game_mining.gfx"));

        Assert.IsTrue(session.ShouldLog("FRServer_Connect"));
        Assert.IsFalse(session.ShouldLog("FRServer_Connect"), "The same name twice.");

        for (var i = 0; i < FlashMinigameSession.MaxLoggedMessageNames * 2; i++)
            session.ShouldLog("Name" + i);

        Assert.IsFalse(session.ShouldLog("OneMore"), "A client sending endless new names can't flood the log.");
    }
}
