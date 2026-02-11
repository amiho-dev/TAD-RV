// ───────────────────────────────────────────────────────────────────────────
// MulticastDiscovery.cs — DC-Less Multi-Lab Discovery via UDP Multicast
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Zero-configuration student discovery for environments with no Domain
// Controller. Uses UDP Multicast group 239.1.1.1:17421 for:
//
//   1. Students broadcast a heartbeat every 3 seconds with their RoomID
//   2. Teacher listens and filters students by matching RoomID
//   3. No DNS, no DHCP reservation, no AD — works on flat L2 networks
//
// RoomID assignment:
//   - From registry: HKLM\SOFTWARE\TAD\RoomID
//   - Fallback: first segment of PC name (e.g., "LAB1-PC04" → "LAB1")
//
// Wire format (JSON over UDP, max 512 bytes):
//   { "v":1, "room":"LAB1", "host":"LAB1-PC04", "ip":"10.0.1.40",
//     "port":17420, "role":"student", "ts":1700000000 }
//
// Multicast TTL = 1 (single LAN segment). IGMPv3 snooping-friendly.
// ───────────────────────────────────────────────────────────────────────────

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace TadBridge.Networking;

// ═══════════════════════════════════════════════════════════════════════════
// Discovery Packet
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// JSON payload transmitted via UDP multicast for discovery.
/// Kept small (< 512 bytes) to fit in a single UDP datagram.
/// </summary>
public sealed class DiscoveryPacket
{
    [JsonPropertyName("v")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("room")]
    public string RoomId { get; set; } = "";

    [JsonPropertyName("host")]
    public string Hostname { get; set; } = "";

    [JsonPropertyName("ip")]
    public string IpAddress { get; set; } = "";

    [JsonPropertyName("port")]
    public int TcpPort { get; set; } = TadTcpListener.ListenPort;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "student"; // "student" | "teacher"

    [JsonPropertyName("ts")]
    public long TimestampUnix { get; set; }
}

// ═══════════════════════════════════════════════════════════════════════════
// Discovered Peer
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Represents a peer discovered via multicast. Kept alive by heartbeats.
/// </summary>
public sealed class DiscoveredPeer
{
    public required string RoomId { get; init; }
    public required string Hostname { get; init; }
    public required string IpAddress { get; init; }
    public required int TcpPort { get; init; }
    public required string Role { get; init; }
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;

    /// <summary>True if this peer hasn't sent a heartbeat in > 10 seconds.</summary>
    public bool IsStale => (DateTime.UtcNow - LastSeen).TotalSeconds > 10;
}

// ═══════════════════════════════════════════════════════════════════════════
// Multicast Discovery Service
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Background service that handles UDP multicast discovery.
///
/// Student mode: sends heartbeats every 3s on 239.1.1.1:17421
/// Both modes:   listens for incoming heartbeats and maintains a peer table
///
/// Teacher reads the peer table to populate the student grid.
/// </summary>
public sealed class MulticastDiscovery : BackgroundService
{
    private readonly ILogger<MulticastDiscovery> _log;

    // Multicast group configuration
    public static readonly IPAddress MulticastGroup = IPAddress.Parse("239.1.1.1");
    public const int MulticastPort = 17421;
    public const int HeartbeatIntervalMs = 3000;
    public const int PeerTimeoutSec = 10;    // Remove peers after 10s silence
    public const int MulticastTtl = 1;       // Single LAN segment

    // Peer table — key: "ip:port"
    private readonly Dictionary<string, DiscoveredPeer> _peers = new();
    private readonly object _peerLock = new();

    // Local identity
    private string _roomId = "";
    private string _localIp = "";
    private string _role = "student";

    /// <summary>Event raised when a new student is discovered.</summary>
    public event Action<DiscoveredPeer>? OnPeerDiscovered;

    /// <summary>Event raised when a peer goes stale and is removed.</summary>
    public event Action<DiscoveredPeer>? OnPeerLost;

    public MulticastDiscovery(ILogger<MulticastDiscovery> log)
    {
        _log = log;
    }

    // ─── Public API ───────────────────────────────────────────────────

    /// <summary>
    /// Get a snapshot of all currently known peers, optionally filtered by RoomID.
    /// </summary>
    public List<DiscoveredPeer> GetPeers(string? roomFilter = null)
    {
        lock (_peerLock)
        {
            var q = _peers.Values.Where(p => !p.IsStale);
            if (!string.IsNullOrEmpty(roomFilter))
                q = q.Where(p => p.RoomId.Equals(roomFilter, StringComparison.OrdinalIgnoreCase));
            return q.ToList();
        }
    }

    /// <summary>
    /// Get all distinct Room IDs currently visible on the network.
    /// </summary>
    public List<string> GetRoomIds()
    {
        lock (_peerLock)
        {
            return _peers.Values
                .Where(p => !p.IsStale)
                .Select(p => p.RoomId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(r => r)
                .ToList();
        }
    }

    /// <summary>Override the role (e.g., "teacher" for the Teacher app).</summary>
    public void SetRole(string role) => _role = role;

    /// <summary>Override the RoomID at runtime.</summary>
    public void SetRoomId(string roomId) => _roomId = roomId;

    // ─── Background Execution ─────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _roomId = ResolveRoomId();
        _localIp = ResolveLocalIp();

        _log.LogInformation(
            "Multicast discovery: group={Group}:{Port}, room={Room}, role={Role}",
            MulticastGroup, MulticastPort, _roomId, _role);

        // Start sender + receiver in parallel
        var sender = Task.Run(() => HeartbeatSenderLoop(ct), ct);
        var receiver = Task.Run(() => HeartbeatReceiverLoop(ct), ct);
        var pruner = Task.Run(() => PeerPrunerLoop(ct), ct);

        await Task.WhenAll(sender, receiver, pruner);
    }

    // ─── Heartbeat Sender ─────────────────────────────────────────────

    private async Task HeartbeatSenderLoop(CancellationToken ct)
    {
        using var client = new UdpClient();
        client.Client.SetSocketOption(SocketOptionLevel.IP,
            SocketOptionName.MulticastTimeToLive, MulticastTtl);

        // Allow multiple processes on same machine (teacher + student test)
        client.Client.SetSocketOption(SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress, true);

        var endpoint = new IPEndPoint(MulticastGroup, MulticastPort);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var packet = new DiscoveryPacket
                {
                    RoomId = _roomId,
                    Hostname = Environment.MachineName,
                    IpAddress = _localIp,
                    TcpPort = TadTcpListener.ListenPort,
                    Role = _role,
                    TimestampUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                var json = JsonSerializer.SerializeToUtf8Bytes(packet);
                await client.SendAsync(json, json.Length, endpoint);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Heartbeat send failed");
            }

            await Task.Delay(HeartbeatIntervalMs, ct);
        }
    }

    // ─── Heartbeat Receiver ───────────────────────────────────────────

    private async Task HeartbeatReceiverLoop(CancellationToken ct)
    {
        using var client = new UdpClient();
        client.Client.SetSocketOption(SocketOptionLevel.Socket,
            SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
        client.JoinMulticastGroup(MulticastGroup);

        _log.LogInformation("Joined multicast group {Group}", MulticastGroup);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await client.ReceiveAsync(ct);
                ProcessIncomingPacket(result.Buffer, result.RemoteEndPoint);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Multicast receive error");
            }
        }
    }

    private void ProcessIncomingPacket(byte[] data, IPEndPoint sender)
    {
        try
        {
            var packet = JsonSerializer.Deserialize<DiscoveryPacket>(data);
            if (packet == null || packet.Version != 1) return;

            // Ignore our own packets
            if (packet.Hostname == Environment.MachineName &&
                packet.IpAddress == _localIp)
                return;

            var key = $"{packet.IpAddress}:{packet.TcpPort}";

            lock (_peerLock)
            {
                if (_peers.TryGetValue(key, out var existing))
                {
                    existing.LastSeen = DateTime.UtcNow;
                }
                else
                {
                    var peer = new DiscoveredPeer
                    {
                        RoomId = packet.RoomId,
                        Hostname = packet.Hostname,
                        IpAddress = packet.IpAddress,
                        TcpPort = packet.TcpPort,
                        Role = packet.Role,
                        LastSeen = DateTime.UtcNow
                    };
                    _peers[key] = peer;
                    _log.LogInformation("Discovered: {Host} ({Ip}) in room {Room}",
                        peer.Hostname, peer.IpAddress, peer.RoomId);
                    OnPeerDiscovered?.Invoke(peer);
                }
            }
        }
        catch (JsonException)
        {
            // Malformed packet — ignore
        }
    }

    // ─── Peer Pruner ──────────────────────────────────────────────────

    private async Task PeerPrunerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(5000, ct); // Prune every 5 seconds

            lock (_peerLock)
            {
                var stale = _peers
                    .Where(kv => kv.Value.IsStale)
                    .Select(kv => kv.Key)
                    .ToList();

                foreach (var key in stale)
                {
                    if (_peers.Remove(key, out var peer))
                    {
                        _log.LogInformation("Peer lost: {Host} ({Ip})",
                            peer.Hostname, peer.IpAddress);
                        OnPeerLost?.Invoke(peer);
                    }
                }
            }
        }
    }

    // ─── RoomID Resolution ────────────────────────────────────────────

    /// <summary>
    /// Resolve the RoomID for this machine:
    ///   1. Registry: HKLM\SOFTWARE\TAD\RoomID
    ///   2. Fallback: first segment of PC name (e.g., "LAB1-PC04" → "LAB1")
    /// </summary>
    private static string ResolveRoomId()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\TAD");
            var val = key?.GetValue("RoomID") as string;
            if (!string.IsNullOrWhiteSpace(val))
                return val.Trim();
        }
        catch { /* Registry not available or access denied */ }

        // Fallback: extract room prefix from hostname
        var name = Environment.MachineName;
        int sep = name.IndexOfAny(['-', '_']);
        return sep > 0 ? name[..sep] : name;
    }

    /// <summary>Get the local LAN IP address.</summary>
    private static string ResolveLocalIp()
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 80);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch { return "127.0.0.1"; }
    }
}
