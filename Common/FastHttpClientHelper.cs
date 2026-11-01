using System.Net;
using System.Net.Sockets;

namespace PixousDocIntelligence.Common;

/// <summary>
/// High-performance HTTP client helper that enforces IPv4-first DNS resolution.
/// 
/// PROBLEM CONTEXT:
/// ----------------
/// By default on Windows, network adapters have IPv6 enabled. When connecting to Google APIs
/// (e.g. generativelanguage.googleapis.com), DNS returns up to 8 IPv6 addresses followed by IPv4 addresses.
/// If the host machine's ISP or local network router does not have an active IPv6 internet route,
/// standard .NET SocketsHttpHandler attempts each IPv6 address in sequence.
/// Windows spends ~21 seconds waiting for a TCP SYN timeout on each unreachable IPv6 address:
/// 
///       8 IPv6 addresses × 21 seconds = ~168 seconds delay before falling back to IPv4!
/// 
/// This creates an apparent 3-minute freeze on every LLM API call and leads to HTTP 499 / 500 client timeouts.
/// 
/// SOLUTION:
/// ---------
/// This helper configures SocketsHttpHandler with a custom ConnectCallback that explicitly resolves
/// IPv4 addresses (AddressFamily.InterNetwork) first. The connection to Google's IPv4 endpoint succeeds
/// in ~20-250 milliseconds instead of 170 seconds, while maintaining safe fallbacks if only IPv6 is present.
/// </summary>
public static class FastHttpClientHelper
{
    /// <summary>
    /// Creates a customized <see cref="SocketsHttpHandler"/> that resolves IPv4 addresses first.
    /// Used by Program.cs to configure the central IHttpClientFactory and by LLM clients.
    /// </summary>
    /// <param name="connectTimeout">
    /// Maximum time allowed to establish a TCP connection before failing (defaults to 5 seconds).
    /// </param>
    /// <returns>A pre-configured <see cref="SocketsHttpHandler"/> instance.</returns>
    public static SocketsHttpHandler CreateFastSocketsHandler(TimeSpan? connectTimeout = null)
    {
        return new SocketsHttpHandler
        {
            // Limit TCP connection handshake time to 5 seconds so threads never hang indefinitely
            ConnectTimeout = connectTimeout ?? TimeSpan.FromSeconds(5),

            // Reuse and multiplex connections over HTTP/2 when supported by the server
            EnableMultipleHttp2Connections = true,

            // Intercept socket creation and connection establishment
            ConnectCallback = async (context, cancellationToken) =>
            {
                IPAddress[] ips;
                try
                {
                    // STEP 1: Query DNS specifically for IPv4 (InterNetwork) addresses first.
                    // This directly bypasses unroutable IPv6 addresses that cause 21s TCP SYN timeouts.
                    ips = await Dns.GetHostAddressesAsync(
                        context.DnsEndPoint.Host, 
                        AddressFamily.InterNetwork, 
                        cancellationToken);
                }
                catch
                {
                    // If IPv4 resolution fails or encounters an exception, fall through to fallback
                    ips = Array.Empty<IPAddress>();
                }

                // STEP 2: Fallback to standard DNS resolution if no IPv4 addresses were returned
                // (e.g. for pure IPv6 networks or local environments)
                if (ips.Length == 0)
                {
                    ips = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                }

                // If no addresses could be found at all, throw a standard HostNotFound socket exception
                if (ips.Length == 0)
                {
                    throw new SocketException((int)SocketError.HostNotFound);
                }

                // STEP 3: Create a standard TCP stream socket
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
                {
                    // Disable Nagle's algorithm for low-latency transmission of JSON requests
                    NoDelay = true
                };

                try
                {
                    // Connect immediately to the first resolved IPv4 address and target port
                    await socket.ConnectAsync(ips[0], context.DnsEndPoint.Port, cancellationToken);

                    // Return an open NetworkStream configured to dispose the socket when closed
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    // Clean up socket descriptor immediately on connection failure to avoid socket leaks
                    socket.Dispose();
                    throw;
                }
            }
        };
    }

    /// <summary>
    /// Convenience factory to create a standalone <see cref="HttpClient"/> configured with the fast IPv4 handler.
    /// Used as fallback when an IHttpClientFactory is not available via dependency injection.
    /// </summary>
    /// <param name="timeout">Request timeout (defaults to 10 minutes for large document batch processing).</param>
    /// <returns>A configured <see cref="HttpClient"/> instance.</returns>
    public static HttpClient CreateClient(TimeSpan? timeout = null)
    {
        return new HttpClient(CreateFastSocketsHandler())
        {
            Timeout = timeout ?? TimeSpan.FromMinutes(10)
        };
    }
}
