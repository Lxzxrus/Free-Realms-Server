using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sanctuary.Core.Helpers;
using Sanctuary.Core.IO;
using Sanctuary.Packet;
using Sanctuary.Packet.Common.Attributes;

namespace Sanctuary.Gateway.Handlers;

[PacketHandler]
public static class BaseFishingPacketHandler
{
    /// <summary>The client asks for its fishing configuration (no packet class: nothing in it is read).</summary>
    private const short RegisterPlayerRequestOpCode = 2;

    private static ILogger _logger = null!;

    public static void ConfigureServices(IServiceProvider serviceProvider)
    {
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        _logger = loggerFactory.CreateLogger(nameof(BaseFishingPacketHandler));
    }

    public static bool HandlePacket(GatewayConnection connection, PacketReader reader)
    {
        if (!reader.TryRead(out short opCode))
        {
            _logger.LogError("Failed to read opcode from packet. ( Data: {data} )", PacketLog.Hex(reader.Span));
            return false;
        }

        // Each packet reads its own header again, so it gets the whole payload.
        return opCode switch
        {
            RegisterPlayerRequestOpCode => FishingHandler.HandleRegisterPlayer(connection),
            FishingPacketCastAnimRequest.OpCode => FishingHandler.HandleCastAnim(connection, reader.Span),
            FishingPacketCastRequest.OpCode => FishingHandler.HandleCast(connection, reader.Span),
            FishingPacketReelInRequest.OpCode => FishingHandler.HandleReelIn(connection, reader.Span),
            FishingPacketSpecialRequest.OpCode => FishingHandler.HandleSpecial(connection, reader.Span),
            _ => false
        };
    }
}
