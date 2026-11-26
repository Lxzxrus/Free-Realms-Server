using System;
using System.IO;
using System.Text.RegularExpressions;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sanctuary.Game.Tests;

/// <summary>
/// The Gateway can't be started in a test and a Player can't be built, so the wiring of the report handlers is checked in
/// the source, the way <see cref="ZoneMovePacingTests"/> checks zone moves. The lessons it guards are in CLAUDE.md: a
/// handler that exists but isn't routed does nothing, and a static handler's field that is never assigned in
/// ConfigureServices compiles and crashes at runtime.
/// </summary>
[TestClass]
public sealed class ReportRoutingTests
{
    private static string Source(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var path = Path.Combine(directory.FullName, relativePath);

            if (File.Exists(path) && Directory.Exists(Path.Combine(directory.FullName, "Sanctuary.Gateway", "Handlers")))
                return File.ReadAllText(path);
        }

        throw new FileNotFoundException(relativePath);
    }

    private static string Squash(string text) => Regex.Replace(text, @"\s+", " ");

    [TestMethod]
    public void TunneledDispatcher_RoutesOpCode68And83ToTheHandlers()
    {
        var dispatcher = Squash(Source("Sanctuary.Gateway/Handlers/PacketTunneledClientPacketHandler.cs"));

        StringAssert.Contains(dispatcher, "BaseReportPacket.OpCode => BaseReportPacketHandler.HandlePacket(connection, reader),");
        StringAssert.Contains(dispatcher,
            "BugSubmissionPacketAddBug.OpCode => BugSubmissionPacketAddBugHandler.HandlePacket(connection, packetTunneledClientPacket.Payload),");
    }

    [TestMethod]
    public void ReportFamilyHandler_RoutesBothSubOpCodes_AndTheOpCodesAre68And83()
    {
        var family = Squash(Source("Sanctuary.Gateway/Handlers/BaseReportPacketHandler.cs"));

        StringAssert.Contains(family, "ReportPacketReportPlayer.OpCode => ReportPacketReportPlayerHandler.HandlePacket(connection, reader.Span),");
        StringAssert.Contains(family, "ReportPacketReportHouse.OpCode => ReportPacketReportHouseHandler.HandlePacket(connection, reader.Span),");

        Assert.AreEqual(68, Sanctuary.Packet.BaseReportPacket.OpCode);
        Assert.AreEqual(1, Sanctuary.Packet.ReportPacketReportPlayer.OpCode);
        Assert.AreEqual(3, Sanctuary.Packet.ReportPacketReportHouse.OpCode);
        Assert.AreEqual(83, Sanctuary.Packet.BugSubmissionPacketAddBug.OpCode);
        Assert.AreEqual(1, Sanctuary.Packet.BugSubmissionPacketAddBug.SubOpCode);
    }

    [TestMethod]
    [DataRow("Sanctuary.Gateway/Handlers/BaseReportPacket/ReportPacketReportPlayerHandler.cs")]
    [DataRow("Sanctuary.Gateway/Handlers/BaseReportPacket/ReportPacketReportHouseHandler.cs")]
    [DataRow("Sanctuary.Gateway/Handlers/BugSubmissionPacketAddBugHandler.cs")]
    public void EachHandlerAssignsItsServicesInConfigureServices(string file)
    {
        var source = Squash(Source(file));

        StringAssert.Contains(source, "[PacketHandler]");
        StringAssert.Contains(source, "public static void ConfigureServices(IServiceProvider serviceProvider)");
        StringAssert.Contains(source, "_reportDesk = serviceProvider.GetRequiredService<ReportDesk>();");
        StringAssert.Contains(source, "_logger = loggerFactory.CreateLogger(");
    }

    [TestMethod]
    public void ReportFamilyHandler_AssignsItsLogger()
    {
        var source = Squash(Source("Sanctuary.Gateway/Handlers/BaseReportPacketHandler.cs"));

        StringAssert.Contains(source, "[PacketHandler]");
        StringAssert.Contains(source, "_logger = loggerFactory.CreateLogger(nameof(BaseReportPacketHandler));");
    }

    [TestMethod]
    public void GatewayRegistersTheReportServices()
    {
        var program = Squash(Source("Sanctuary.Gateway/Program.cs"));

        StringAssert.Contains(program, "AddSingleton<ReportDesk>()");
        StringAssert.Contains(program, "new ReportService(");
        StringAssert.Contains(program, "AddSingleton<IReportNotifier>(");
        StringAssert.Contains(program, "AddHostedService<ReportNotifierService>()");
        StringAssert.Contains(program, "AddHostedService<ReportRetentionService>()");
        StringAssert.Contains(program, "AddDiscordWebhookClient()");
        StringAssert.Contains(program, "AddOptions<ReportsOptions>()");
    }

    [TestMethod]
    public void ChatHandler_RecordsAcceptedChatAfterTheMuteCheck()
    {
        var chat = Source("Sanctuary.Gateway/Handlers/PacketChat/PacketChatHandler.cs");

        var muted = chat.IndexOf("IsMuted()", StringComparison.Ordinal);
        var recorded = chat.IndexOf("ChatEvidence.Add(", StringComparison.Ordinal);
        var relayed = chat.IndexOf("switch (packet.Channel)", StringComparison.Ordinal);

        Assert.IsTrue(muted >= 0 && recorded > muted && relayed > recorded, "a muted player's chat is not recorded as accepted");
    }

    [TestMethod]
    public void TheWebhookIsOffInEveryTrackedFile()
    {
        var gateway = Source("Sanctuary.Gateway/gateway.json");
        var example = Source("Sanctuary.Gateway/gateway.local.example.json");

        Assert.IsTrue(Regex.IsMatch(gateway, "\"DiscordWebhookUrl\"\\s*:\\s*\"\""), "tracked default must be empty");
        StringAssert.Contains(example, "DiscordWebhookUrl");
        Assert.IsFalse(Regex.IsMatch(example + gateway, "webhooks/\\d"), "no real webhook address in a tracked file");
    }
}
