using System.Net;
using System.Net.Sockets;

using Microsoft.AspNetCore.Http;

namespace Sanctuary.WebAPI.Security;

public static class ClientAddress
{
    /// <summary>
    /// The key that per-address limits are counted under. IPv6 clients are grouped by their /64, because one
    /// home connection usually owns a whole /64 and could otherwise rotate through addresses freely.
    /// </summary>
    public static string GetKey(HttpContext context)
    {
        return GetKey(context.Connection.RemoteIpAddress);
    }

    public static string GetKey(IPAddress? address)
    {
        if (address is null)
            return "unknown";

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return address.ToString();

        var bytes = address.GetAddressBytes();

        for (var i = 8; i < bytes.Length; i++)
            bytes[i] = 0;

        return $"{new IPAddress(bytes)}/64";
    }
}
