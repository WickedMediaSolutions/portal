using System.Net;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Portal.Networking.Tests;

/// <summary>
/// Minimal local WebSocket server for testing <see cref="WebSocketConnection"/>
/// without requiring a real ROP server.
/// </summary>
public sealed class WebSocketTestHarness : IAsyncDisposable
{
    private readonly Func<HttpContext, WebSocket, CancellationToken, Task> _handler;
    private IHost? _host;
    private int _port;
    private bool _started;

    /// <summary>
    /// A simple echo handler: reads each message and sends it back.
    /// </summary>
    public static readonly Func<HttpContext, WebSocket, CancellationToken, Task> EchoHandler =
        async (context, ws, ct) =>
        {
            var buffer = new byte[4096];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Echo closing", CancellationToken.None);
                    break;
                }
                await ws.SendAsync(
                    new ArraySegment<byte>(buffer, 0, result.Count),
                    result.MessageType, result.EndOfMessage, ct);
            }
        };

    /// <summary>
    /// Echoes back complete text messages (assembling fragments).
    /// </summary>
    public static readonly Func<HttpContext, WebSocket, CancellationToken, Task> TextEchoHandler =
        async (context, ws, ct) =>
        {
            var buffer = new byte[4096];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result;
                using var ms = new MemoryStream();
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        return;
                    }
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                var data = ms.ToArray();
                await ws.SendAsync(new ArraySegment<byte>(data), WebSocketMessageType.Text, true, ct);
            }
        };
/// <summary>
    /// Accepts then immediately closes (simulates server disconnect).
    /// </summary>
    public static readonly Func<HttpContext, WebSocket, CancellationToken, Task> CloseImmediatelyHandler =
        async (context, ws, ct) =>
        {
            await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing", CancellationToken.None);
        };

    /// <summary>
    /// Sends malformed (non-JSON) data first, then echoes normally.
    /// </summary>
    public static readonly Func<HttpContext, WebSocket, CancellationToken, Task> SendMalformedHandler =
        async (context, ws, ct) =>
        {
            var malformed = Encoding.UTF8.GetBytes("this is not valid json {{{");
            await ws.SendAsync(new ArraySegment<byte>(malformed), WebSocketMessageType.Text, true, ct);

            var buffer = new byte[4096];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close) break;
                await ws.SendAsync(new ArraySegment<byte>(buffer, 0, result.Count),
                    result.MessageType, result.EndOfMessage, ct);
            }
        };

    public WebSocketTestHarness(Func<HttpContext, WebSocket, CancellationToken, Task> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }
public async Task StartAsync()
    {
        if (_started)
            throw new InvalidOperationException("Test harness is already started.");

        var handler = _handler;

        _host = new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder.UseKestrel(options =>
                {
                    options.Listen(IPAddress.Loopback, 0);
                });
                webBuilder.Configure(app =>
                {
                    app.UseWebSockets(new WebSocketOptions
                    {
                        KeepAliveInterval = TimeSpan.FromSeconds(5)
                    });
                    app.Run(async context =>
                    {
                        if (context.Request.Path == "/ws" && context.WebSockets.IsWebSocketRequest)
                        {
                            using var ws = await context.WebSockets.AcceptWebSocketAsync();
                            await handler(context, ws, context.RequestAborted);
                        }
                        else
                        {
                            context.Response.StatusCode = 400;
                            await context.Response.WriteAsync("WebSocket endpoint at /ws");
                        }
                    });
                });
            })
            .Build();

        await _host.StartAsync();

        var addresses = _host.Services.GetRequiredService<
            Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>();

        var address = addresses?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("No server addresses found.");

        _port = new Uri(address).Port;
        _started = true;
    }

    public Uri GetUri() => new($"ws://localhost:{_port}/ws");

    public async Task StopAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
        }
        _started = false;
    }

    public async ValueTask DisposeAsync()
    {
        if (_started) await StopAsync();
    }
}