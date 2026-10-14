using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Game.Helpers;

namespace Sanctuary.Game.Tests;

[TestClass]
public sealed class LoginAdmissionTests
{
    [TestMethod]
    public void FirstLoginOfAnOfflineCharacter_IsAdmitted()
    {
        Assert.AreEqual(LoginRefusal.None, LoginAdmission.Check(connectionHasPlayer: false, characterOnline: false));
    }

    [TestMethod]
    public void SecondLoginOnTheSameConnection_IsRefused()
    {
        Assert.AreEqual(LoginRefusal.ConnectionAlreadyLoggedIn, LoginAdmission.Check(connectionHasPlayer: true, characterOnline: false));
    }

    [TestMethod]
    public void CharacterAlreadyPlayedByAnotherConnection_IsRefused()
    {
        Assert.AreEqual(LoginRefusal.CharacterAlreadyOnline, LoginAdmission.Check(connectionHasPlayer: false, characterOnline: true));
    }
}
