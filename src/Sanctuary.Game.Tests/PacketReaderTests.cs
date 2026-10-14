using System;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Sanctuary.Core.IO;

namespace Sanctuary.Game.Tests;

/// <summary>
/// A hostile client decides every length prefix. A string whose length is negative used to throw out of
/// <see cref="PacketReader.TryRead(out string)"/> instead of failing the read (task 30).
/// </summary>
[TestClass]
public sealed class PacketReaderTests
{
    [TestMethod]
    public void NegativeStringLength_FailsTheRead_InsteadOfThrowing()
    {
        var reader = new PacketReader(BitConverter.GetBytes(-1));

        Assert.IsFalse(reader.TryRead(out string value));
        Assert.AreEqual(string.Empty, value);
    }

    [TestMethod]
    public void StringLongerThanThePacket_FailsTheRead()
    {
        var data = new byte[8];
        BitConverter.GetBytes(100).CopyTo(data, 0);
        var reader = new PacketReader(data);

        Assert.IsFalse(reader.TryRead(out string _));
    }

    [TestMethod]
    public void NegativeExactLength_FailsTheRead()
    {
        var reader = new PacketReader(new byte[4]);

        Assert.IsFalse(reader.TryReadExact(-1, out _));
    }
}
