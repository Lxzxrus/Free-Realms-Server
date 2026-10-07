using System;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Launcher.Helpers;

/// <summary>
/// Decides which addresses the launcher may talk to over plain HTTP.
/// Passwords, server manifests and game files all go over HTTP, so anything on the internet must be HTTPS:
/// over plain HTTP, anyone on the path could read the password or swap the game's executable.
/// Plain HTTP is allowed only to this machine and the local network, for testing.
/// </summary>
public static class TransportPolicy
{
    public static bool IsAllowed(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            return false;

        if (uri.Scheme == Uri.UriSchemeHttps)
            return true;

        if (uri.Scheme != Uri.UriSchemeHttp)
            return false;

        return IsLocalHost(uri.IdnHost);
    }

    public static bool IsAllowed(string url)
        => Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsAllowed(uri);

    private static bool IsLocalHost(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        // mDNS names, such as the staging machine's "optiplex.local".
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IPAddress.TryParse(host, out var address))
            return false;

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address))
            return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();

            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();

            // Unique local (fc00::/7) and link-local (fe80::/10).
            return (bytes[0] & 0xFE) == 0xFC || address.IsIPv6LinkLocal;
        }

        return false;
    }
}

/// <summary>
/// Thrown instead of sending a request that <see cref="TransportPolicy"/> refuses.
/// </summary>
public sealed class InsecureTransportException(Uri uri)
    : HttpRequestException($"Refusing to use plain HTTP for {uri.GetLeftPart(UriPartial.Authority)}: only HTTPS is allowed outside the local network.")
{
    public Uri Uri { get; } = uri;
}

/// <summary>
/// Applies <see cref="TransportPolicy"/> to every request an <see cref="HttpClient"/> makes.
/// Redirects from HTTPS to HTTP are already refused by <see cref="SocketsHttpHandler"/>.
/// </summary>
public sealed class TransportPolicyHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null || !TransportPolicy.IsAllowed(request.RequestUri))
            throw new InsecureTransportException(request.RequestUri ?? new Uri("http://unknown"));

        return base.SendAsync(request, cancellationToken);
    }
}
