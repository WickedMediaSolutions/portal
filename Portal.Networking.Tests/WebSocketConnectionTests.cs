using System.Net.WebSockets;
using System.Text.Json;
using Portal.Core.Results;
using Portal.Protocol;
using Xunit;

namespace Portal.Networking.Tests;

/// <summary>
/// Tests for <see cref="WebSocketConnection"/> covering the 9 required scenarios
/// from the frozen specification plus additional edge cases.
/// </summary>
public class WebSocketConnectionTests : IAsyncLifetime
{
    private WebSocketTestHarness _harness = null!;
    private Uri _serverUri = null!;

    public async Task InitializeAsync()
    {
        _harness = new WebSocketTestHarness(WebSocketTestHarness.TextEchoHandler);
        await _harness.StartAsync();
        _serverUri = _harness.GetUri();
    }

    public async Task DisposeAsync()
    {
        await _harness.DisposeAsync();
    }

    /// <summary>
    /// Helper to create a valid test envelope.
    /// </summary>
    private static MessageEnvelope CreateTestEnvelope(string messageType = "test.ping", string? correlationId = null)
    {
        var payload = JsonDocument.Parse("{\"value\":42}").RootElement.Clone();
        return new MessageEnvelope(
            ProtocolVersion.V1,
            MessageCategory.Request,
            messageType,
            correlationId,
            null,
            payload);
    }
// ─── Scenario 1: Connection-State Transitions ────────────────────

    [Fact]
    public async Task ConnectAsync_FromDisconnected_TransitionsToConnected()
    {
        var states = new List<ConnectionState>();
        await using var conn = new WebSocketConnection();
        conn.StateChanged += (_, args) => states.Add(args.NewState);

        var result = await conn.ConnectAsync(_serverUri);

        Assert.True(result.IsSuccess);
        Assert.Contains(ConnectionState.Connecting, states);
        Assert.Contains(ConnectionState.Connected, states);
        Assert.Equal(ConnectionState.Connected, conn.State);
    }

    [Fact]
    public async Task DisconnectAsync_FromConnected_TransitionsToDisconnected()
    {
        var states = new List<ConnectionState>();
        await using var conn = new WebSocketConnection();
        conn.StateChanged += (_, args) => states.Add(args.NewState);

        await conn.ConnectAsync(_serverUri);
        var result = await conn.DisconnectAsync();

        Assert.True(result.IsSuccess);
        Assert.Contains(ConnectionState.Disconnecting, states);
        Assert.Contains(ConnectionState.Disconnected, states);
        Assert.Equal(ConnectionState.Disconnected, conn.State);
    }

    // ─── Scenario 2: Cancellation Behavior ──────────────────────────

    [Fact]
    public async Task ConnectAsync_WithCancelledToken_ReturnsFailure()
    {
        await using var conn = new WebSocketConnection();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await conn.ConnectAsync(_serverUri, cts.Token);

        Assert.True(result.IsFailure);
        Assert.Contains("CANCELLED", result.Errors[0].Code);
    }

    [Fact]
    public async Task ReceiveAsync_WithCancelledToken_ReturnsFailure()
    {
        await using var conn = new WebSocketConnection();
        await conn.ConnectAsync(_serverUri);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await conn.ReceiveAsync(cts.Token);
        Assert.True(result.IsFailure);
    }

    // ─── Scenario 3: Graceful Disconnect Behavior ────────────────────

    [Fact]
    public async Task DisconnectAsync_CleansUpAndAllowsReconnect()
    {
        await using var conn = new WebSocketConnection();

        await conn.ConnectAsync(_serverUri);
        Assert.Equal(ConnectionState.Connected, conn.State);

        await conn.DisconnectAsync();
        Assert.Equal(ConnectionState.Disconnected, conn.State);

        // Should be able to reconnect
        var result2 = await conn.ConnectAsync(_serverUri);
        Assert.True(result2.IsSuccess);
        Assert.Equal(ConnectionState.Connected, conn.State);
    }

    // ─── Scenario 4: Send-Path Serialization Integration ─────────────

    [Fact]
    public async Task SendAsync_ValidEnvelope_SerializesAndSendsOverWebSocket()
    {
        await using var conn = new WebSocketConnection();
        await conn.ConnectAsync(_serverUri);

        var envelope = CreateTestEnvelope("test.hello", "corr-001");
        var sendResult = await conn.SendAsync(envelope);

        Assert.True(sendResult.IsSuccess);

        // The echo server should send it back
        var receiveResult = await conn.ReceiveAsync();
        Assert.True(receiveResult.IsSuccess);
        Assert.Equal("test.hello", receiveResult.Value.MessageType);
        Assert.Equal("corr-001", receiveResult.Value.CorrelationId);
    }

    [Fact]
    public async Task SendAsync_WhenNotConnected_ReturnsFailure()
    {
        await using var conn = new WebSocketConnection();

        var result = await conn.SendAsync(CreateTestEnvelope());

        Assert.True(result.IsFailure);
        Assert.Contains("NOT_CONNECTED", result.Errors[0].Code);
    }

    // ─── Scenario 5: Receive-Path Protocol Parsing Integration ───────

    [Fact]
    public async Task ReceiveAsync_AfterSend_ReturnsValidatedEnvelope()
    {
        await using var conn = new WebSocketConnection();
        await conn.ConnectAsync(_serverUri);

        var envelope = CreateTestEnvelope("test.query");
        await conn.SendAsync(envelope);

        var receiveResult = await conn.ReceiveAsync();
        Assert.True(receiveResult.IsSuccess);

        var received = receiveResult.Value;
        Assert.Equal(ProtocolVersion.V1, received.Version);
        Assert.Equal(MessageCategory.Request, received.Category);
        Assert.Equal("test.query", received.MessageType);
    }

    [Fact]
    public async Task ReceiveAsync_WhenNotConnected_ReturnsFailure()
    {
        await using var conn = new WebSocketConnection();

        var result = await conn.ReceiveAsync();

        Assert.True(result.IsFailure);
    }

    // ─── Scenario 6: Malformed Received Message Handling ─────────────

    [Fact]
    public async Task ReceiveLoop_MalformedMessage_DoesNotPropagateToConsumer()
    {
        // Use a harness that sends malformed data first, then a valid message
        await using var malformedHarness = new WebSocketTestHarness(
            WebSocketTestHarness.SendMalformedHandler);
        await malformedHarness.StartAsync();
        var malformedUri = malformedHarness.GetUri();

        await using var conn = new WebSocketConnection();
        await conn.ConnectAsync(malformedUri);

        // Give the server time to send the malformed data and the client
        // receive loop time to process and drop it
        await Task.Delay(200);

        // Send a valid message — server echoes it after the malformed one
        var validEnvelope = CreateTestEnvelope("test.after.malformed");
        var sendResult = await conn.SendAsync(validEnvelope);
        Assert.True(sendResult.IsSuccess,
            $"Send failed: {string.Join("; ", sendResult.Errors.Select(e => e.ToString()))}");

        // Receive — should get only the valid message (malformed is dropped)
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var receiveResult = await conn.ReceiveAsync(timeoutCts.Token);
        Assert.True(receiveResult.IsSuccess,
            $"Expected success but got failure: {string.Join("; ", receiveResult.Errors.Select(e => e.ToString()))}");
        Assert.Equal("test.after.malformed", receiveResult.Value.MessageType);
    }
// ─── Scenario 7: Network Failure Handling ────────────────────────

    [Fact]
    public async Task ServerClosesConnection_TransitionsToDisconnected()
    {
        // Use a harness that immediately closes
        await using var closingHarness = new WebSocketTestHarness(
            WebSocketTestHarness.CloseImmediatelyHandler);
        await closingHarness.StartAsync();
        var closingUri = closingHarness.GetUri();

        var states = new List<ConnectionState>();
        await using var conn = new WebSocketConnection();
        conn.StateChanged += (_, args) => states.Add(args.NewState);

        await conn.ConnectAsync(closingUri);

        // Give the server's close frame time to propagate
        // The connection should eventually detect the close
        await Task.Delay(500);

        Assert.Contains(ConnectionState.Disconnected, states);
    }

    [Fact]
    public async Task SendAsync_AfterServerDisconnect_ReturnsFailure()
    {
        await using var closingHarness = new WebSocketTestHarness(
            WebSocketTestHarness.CloseImmediatelyHandler);
        await closingHarness.StartAsync();
        var closingUri = closingHarness.GetUri();

        await using var conn = new WebSocketConnection();
        await conn.ConnectAsync(closingUri);

        // Wait for close to propagate
        await Task.Delay(500);

        var result = await conn.SendAsync(CreateTestEnvelope());
        Assert.True(result.IsFailure);
    }

    // ─── Scenario 8: No Unhandled Exception Escapes Receive Loop ──────

    [Fact]
    public async Task ReceiveLoop_MultipleMalformedMessages_ContinuesProcessing()
    {
        // Use the malformed handler: server sends malformed, then echoes
        await using var malformedHarness = new WebSocketTestHarness(
            WebSocketTestHarness.SendMalformedHandler);
        await malformedHarness.StartAsync();
        var malformedUri = malformedHarness.GetUri();

        await using var conn = new WebSocketConnection();
        await conn.ConnectAsync(malformedUri);

        // Give the server time to send the malformed data
        await Task.Delay(200);

        // Send 3 valid messages; each should be echoed back
        for (int i = 0; i < 3; i++)
        {
            var envelope = CreateTestEnvelope($"test.round.{i}");
            await conn.SendAsync(envelope);
        }

        // Should receive exactly 3 valid messages (malformed ones are dropped)
        var received = new List<MessageEnvelope>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            for (int i = 0; i < 3; i++)
            {
                var result = await conn.ReceiveAsync(cts.Token);
                if (result.IsSuccess)
                    received.Add(result.Value);
            }
        }
        catch (OperationCanceledException) { /* timeout */ }

        Assert.Equal(3, received.Count);
        Assert.All(received, r => Assert.StartsWith("test.round.", r.MessageType));
    }

    // ─── Scenario 9: Multiple Sequential Connect/Disconnect ──────────

    [Fact]
    public async Task ConnectDisconnect_MultipleCycles_EachSucceeds()
    {
        await using var conn = new WebSocketConnection();

        for (int i = 0; i < 3; i++)
        {
            var connectResult = await conn.ConnectAsync(_serverUri);
            Assert.True(connectResult.IsSuccess, $"Connect cycle {i} failed");
            Assert.Equal(ConnectionState.Connected, conn.State);

            var disconnectResult = await conn.DisconnectAsync();
            Assert.True(disconnectResult.IsSuccess, $"Disconnect cycle {i} failed");
            Assert.Equal(ConnectionState.Disconnected, conn.State);
        }
    }

    // ─── Additional Edge Cases ─────────────────────────────────────

    [Fact]
    public async Task ConnectAsync_InvalidScheme_ReturnsFailure()
    {
        await using var conn = new WebSocketConnection();

        var result = await conn.ConnectAsync(new Uri("http://localhost/ws"));

        Assert.True(result.IsFailure);
        Assert.Contains("INVALID_URI", result.Errors[0].Code);
    }

    [Fact]
    public async Task ConnectAsync_AlreadyConnected_ReturnsFailure()
    {
        await using var conn = new WebSocketConnection();
        await conn.ConnectAsync(_serverUri);

        var result = await conn.ConnectAsync(_serverUri);

        Assert.True(result.IsFailure);
        Assert.Contains("INVALID_STATE", result.Errors[0].Code);
    }

    [Fact]
    public async Task DisposeAsync_WhenConnected_GracefullyDisconnects()
    {
        var conn = new WebSocketConnection();
        await conn.ConnectAsync(_serverUri);
        Assert.Equal(ConnectionState.Connected, conn.State);

        await conn.DisposeAsync();

        Assert.Equal(ConnectionState.Disconnected, conn.State);
    }

    [Fact]
    public async Task SendAsync_WithNullEnvelope_ReturnsFailure()
    {
        await using var conn = new WebSocketConnection();
        await conn.ConnectAsync(_serverUri);

        var result = await conn.SendAsync(null!);

        Assert.True(result.IsFailure);
        Assert.Contains("INVALID_MESSAGE", result.Errors[0].Code);
    }

    [Fact]
    public async Task ConnectionStateChanged_EventFiresOnEveryTransition()
    {
        await using var conn = new WebSocketConnection();
        var transitions = new List<(ConnectionState Old, ConnectionState New)>();

        conn.StateChanged += (_, args) =>
            transitions.Add((args.OldState, args.NewState));

        await conn.ConnectAsync(_serverUri);
        await conn.DisconnectAsync();

        // Disconnected → Connecting → Connected → Disconnecting → Disconnected
        Assert.True(transitions.Count >= 3);
        Assert.Contains((ConnectionState.Disconnected, ConnectionState.Connecting), transitions);
        Assert.Contains((ConnectionState.Connecting, ConnectionState.Connected), transitions);
    }

    [Fact]
    public async Task RoundTrip_MultipleEnvelopes_PreservesContent()
    {
        await using var conn = new WebSocketConnection();
        await conn.ConnectAsync(_serverUri);

        var sent = new List<MessageEnvelope>();
        for (int i = 0; i < 5; i++)
        {
            sent.Add(CreateTestEnvelope($"test.rtt.{i}", $"corr-{i}"));
        }

        foreach (var envelope in sent)
        {
            var sendResult = await conn.SendAsync(envelope);
            Assert.True(sendResult.IsSuccess);
        }

        var received = new List<MessageEnvelope>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        for (int i = 0; i < 5; i++)
        {
            var result = await conn.ReceiveAsync(cts.Token);
            Assert.True(result.IsSuccess);
            received.Add(result.Value);
        }

        Assert.Equal(5, received.Count);
        for (int i = 0; i < 5; i++)
        {
            Assert.Equal(sent[i].MessageType, received[i].MessageType);
            Assert.Equal(sent[i].CorrelationId, received[i].CorrelationId);
        }
    }
}