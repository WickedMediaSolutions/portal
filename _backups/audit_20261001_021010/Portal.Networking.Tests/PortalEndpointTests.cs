using System.Text.Json;
using Portal.Networking;
using Portal.Protocol;
using Portal.Services;
using Xunit;

namespace Portal.Networking.Tests;

/// <summary>
/// Covers the connection-endpoint contract: production is the default,
/// localhost is reachable only through the explicit client-side toggle, and
/// there is no automatic fallback between them.
///
/// These tests assert MECHANICALLY what section 15 of the QA plan requires —
/// no production Keystone deployment is contacted to prove it.
/// </summary>
public sealed class PortalEndpointTests
{
    // ─── Production default ───────────────────────────────────────────────

    [Fact]
    public void Default_target_is_production_not_localhost()
    {
        Assert.Equal(PortalEndpointTarget.Production, PortalEndpoints.DefaultTarget);

        // A fresh settings object (a new install, or a missing/corrupt settings
        // file) must resolve to production.
        Assert.Equal(
            PortalEndpointTarget.Production,
            PortalEndpoints.FromToggle(useLocalDevelopmentServer: false));
    }

    [Fact]
    public void Production_endpoint_resolves_to_ritesrpg_com()
    {
        var endpoint = PortalEndpoints.Resolve(PortalEndpointTarget.Production);

        Assert.Equal("ritesrpg.com", endpoint.Host);
        Assert.Equal("/", endpoint.AbsolutePath);
    }

    [Fact]
    public void Production_endpoint_uses_secure_websocket_transport()
    {
        var endpoint = PortalEndpoints.Resolve(PortalEndpointTarget.Production);

        Assert.Equal("wss", endpoint.Scheme);
        Assert.False(PortalEndpoints.IsPlaintextTransport(endpoint));
    }

    [Fact]
    public void Production_endpoint_contains_no_localhost_authority()
    {
        var endpoint = PortalEndpoints.Resolve(PortalEndpointTarget.Production);

        Assert.DoesNotContain("localhost", endpoint.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", endpoint.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(PortalEndpoints.LocalDevelopmentPort.ToString(),
            endpoint.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Production_endpoint_carries_the_standard_https_port_only()
    {
        // A player must never have to think about port numbers, and a
        // production deployment terminates TLS on the standard HTTPS port.
        var endpoint = PortalEndpoints.Resolve(PortalEndpointTarget.Production);
        Assert.Equal(443, endpoint.Port);
    }

    // ─── Local development toggle ────────────────────────────────────────

    [Fact]
    public void Local_toggle_on_resolves_to_localhost_4013()
    {
        var endpoint = PortalEndpoints.Resolve(PortalEndpointTarget.LocalDevelopment);

        Assert.Equal("localhost", endpoint.Host);
        Assert.Equal(4013, endpoint.Port);
        Assert.Equal("ws", endpoint.Scheme);
        Assert.True(PortalEndpoints.IsPlaintextTransport(endpoint));
    }

    [Fact]
    public void Local_toggle_off_and_on_map_to_the_expected_targets()
    {
        Assert.Equal(
            PortalEndpointTarget.Production,
            PortalEndpoints.FromToggle(useLocalDevelopmentServer: false));

        Assert.Equal(
            PortalEndpointTarget.LocalDevelopment,
            PortalEndpoints.FromToggle(useLocalDevelopmentServer: true));
    }

    [Fact]
    public void Toggle_round_trips_through_the_persisted_representation()
    {
        foreach (var target in new[]
                 {
                     PortalEndpointTarget.Production,
                     PortalEndpointTarget.LocalDevelopment,
                 })
        {
            var restored = PortalEndpoints.FromToggle(PortalEndpoints.ToToggle(target));
            Assert.Equal(target, restored);
        }
    }

    [Fact]
    public void Only_the_local_development_endpoint_is_plaintext()
    {
        Assert.True(PortalEndpoints.IsPlaintextTransport(
            PortalEndpoints.Resolve(PortalEndpointTarget.LocalDevelopment)));

        Assert.False(PortalEndpoints.IsPlaintextTransport(
            PortalEndpoints.Resolve(PortalEndpointTarget.Production)));
    }

    // ─── No insecure production fallback ─────────────────────────────────

    [Fact]
    public void There_is_no_insecure_production_variant()
    {
        // ws:// against the production host must not be constructible from the
        // endpoint API at all: production resolves only through wss://.
        var production = PortalEndpoints.Resolve(PortalEndpointTarget.Production);

        Assert.Equal("wss", production.Scheme);
        Assert.NotEqual("ws", production.Scheme);

        // And no ws:// endpoint is ever built for a non-loopback host.
        foreach (var target in Enum.GetValues<PortalEndpointTarget>())
        {
            var endpoint = PortalEndpoints.Resolve(target);
            if (PortalEndpoints.IsPlaintextTransport(endpoint))
            {
                Assert.True(
                    endpoint.IsLoopback,
                    $"Plaintext transport is only permitted on loopback, got '{endpoint}'.");
            }
        }
    }

    [Fact]
    public void Production_selection_is_retained_across_a_reconnect()
    {
        // A reconnect re-resolves from the CURRENT selection rather than
        // replaying a captured endpoint, and production selection survives it.
        var options = BuildOptions();
        options.UseTarget(PortalEndpoints.FromToggle(useLocalDevelopmentServer: false));

        var first = options.ServerUri;

        // Simulate a dropped session followed by a reconnect.
        options.UseTarget(PortalEndpoints.FromToggle(useLocalDevelopmentServer: false));

        Assert.Equal(first, options.ServerUri);
        Assert.Equal("ritesrpg.com", options.ServerUri.Host);
        Assert.Equal("wss", options.ServerUri.Scheme);
    }

    [Fact]
    public void Reconnect_uses_the_currently_selected_endpoint()
    {
        var options = BuildOptions();

        // Start on production, as a default install would.
        options.UseTarget(PortalEndpoints.FromToggle(useLocalDevelopmentServer: false));
        Assert.Equal("ritesrpg.com", options.ServerUri.Host);

        // The developer opts in to localhost; the very next connect uses it.
        options.UseTarget(PortalEndpoints.FromToggle(useLocalDevelopmentServer: true));
        Assert.Equal("localhost", options.ServerUri.Host);
        Assert.Equal(4013, options.ServerUri.Port);

        // Opting back out returns to production — and never leaves the client
        // pointed at localhost.
        options.UseTarget(PortalEndpoints.FromToggle(useLocalDevelopmentServer: false));
        Assert.Equal("ritesrpg.com", options.ServerUri.Host);
        Assert.DoesNotContain("localhost", options.ServerUri.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerUri_cannot_be_set_to_null()
    {
        var options = BuildOptions();
        Assert.Throws<ArgumentNullException>(() => options.ServerUri = null!);
    }

    // ─── Persisted toggle state ──────────────────────────────────────────

    [Fact]
    public void Persisted_toggle_state_survives_a_settings_round_trip()
    {
        // PortalSettings stores this as a plain bool in its JSON file; the
        // serializer behaviour is what "persists across restarts" rests on, so
        // it is asserted directly against the same options PortalSettings uses.
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        foreach (var useLocalDev in new[] { false, true })
        {
            var saved = new { UseLocalDevelopmentServer = useLocalDev };
            var json = JsonSerializer.Serialize(saved, options);
            var loaded = JsonSerializer.Deserialize<PersistedToggle>(json, options);

            Assert.NotNull(loaded);
            Assert.Equal(useLocalDev, loaded!.UseLocalDevelopmentServer);

            // And the persisted value drives the endpoint, with no extra step.
            Assert.Equal(
                useLocalDev ? "localhost" : "ritesrpg.com",
                PortalEndpoints.Resolve(PortalEndpoints.FromToggle(loaded.UseLocalDevelopmentServer)).Host);
        }
    }

    [Fact]
    public void Settings_file_without_the_toggle_key_defaults_to_production()
    {
        // An existing settings.json written before this feature existed has no
        // such key. It must load as production, never as localhost.
        const string legacyJson = """
        { "Version": 1, "WindowWidth": 1280, "WindowHeight": 800, "Username": "someone" }
        """;

        var loaded = JsonSerializer.Deserialize<PersistedToggle>(
            legacyJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(loaded);
        Assert.False(loaded!.UseLocalDevelopmentServer);
        Assert.Equal("ritesrpg.com",
            PortalEndpoints.Resolve(PortalEndpoints.FromToggle(loaded.UseLocalDevelopmentServer)).Host);
    }

    /// <summary>Mirrors the persisted shape of PortalSettings.UseLocalDevelopmentServer.</summary>
    private sealed class PersistedToggle
    {
        public bool UseLocalDevelopmentServer { get; set; }
    }

    // ─── Connection target display ───────────────────────────────────────

    [Fact]
    public void Connection_target_labels_are_concise_and_port_free()
    {
        Assert.Equal("Production", PortalEndpoints.Describe(PortalEndpointTarget.Production));
        Assert.Equal("Local Development", PortalEndpoints.Describe(PortalEndpointTarget.LocalDevelopment));

        // Ordinary players never see a port number in the indicator.
        Assert.DoesNotContain("4013", PortalEndpoints.Describe(PortalEndpointTarget.LocalDevelopment),
            StringComparison.Ordinal);
        Assert.DoesNotContain("://", PortalEndpoints.Describe(PortalEndpointTarget.Production),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_target_is_rejected_rather_than_silently_defaulting()
    {
        // A silent default here would be exactly the kind of hidden fallback
        // this configuration must never have.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PortalEndpoints.Resolve((PortalEndpointTarget)99));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PortalEndpoints.Describe((PortalEndpointTarget)99));
    }

    private static AuthenticationServiceOptions BuildOptions() =>
        new(PortalEndpoints.Resolve(PortalEndpoints.DefaultTarget),
            "Portal",
            "1.0.0",
            Array.Empty<CapabilityInfo>());
}