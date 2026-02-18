// ───────────────────────────────────────────────────────────────────────────
// DiscoveryListener.cs — Lightweight UDP multicast listener for Teacher
//
// (C) 2026 TAD Europe — https://tad-it.eu
//
// Listens on the same multicast group (239.1.1.1:17421) used by the
// student-side MulticastDiscovery service.  When a student heartbeat
// arrives, fires OnStudentDiscovered so the Teacher can auto-add the
// student IP to TcpClientManager — zero-config, no AD required.
//
// Runs on a background thread; call Start() / Stop().
// ───────────────────────────────────────────────────────────────────────────

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TadTeacher.Networking;

/// <summary>
/// Minimal discovery packet — mirrors the Service-side DiscoveryPacket.
/// </summary>
public sealed class DiscoveryPacket
{
    [JsonPropertyName("v")]
    public int Version { get; set; }

    [JsonPropertyName("room")]
    public string RoomId { get; set; } = "";

    [JsonPropertyName("host")]
    public string Hostname { get; set; } = "";

    [JsonPropertyName("ip")]
    public string IpAddress { get; set; } = "";

    [JsonPropertyName("port")]
    public int TcpPort { get; set; } = 17420;

    [JsonPropertyName("role")]
    public string Role { get; set; } = "student";

    [JsonPropertyName("ts")]
    public long TimestampUnix { get; set; }
}

/// <summary>
/// Joins the TAD.RV multicast group and listens for student heartbeats.
/// Fires <see cref="OnStudentDiscovered"/> for each new student IP.
/// </summary>
public sealed class DiscoveryListener : IDisposable
{
    private static readonly IPAddress MulticastGroup = IPAddress.Parse("239.1.1.1");
    private const int MulticastPort = 17421;

    private readonly HashSet<string> _knownIps = new();
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private Thread? _thread;

    /// <summary>
    /// Fired when a previously-unseen student IP is discovered.
    /// Args: (ip, port, hostname, roomId)
    /// </summary>
    public event Action<string, int, string, string>? OnStudentDiscovered;

    /// <summary>
    /// All currently known student IPs (thread-safe snapshot).
    /// </summary>
    public List<string> KnownStudentIps
    {
        get { lock (_lock) return _knownIps.ToList(); }
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _thread = new Thread(ListenLoop) { IsBackground = true, Name = "DiscoveryListener" };
        _thread.Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _thread?.Join(2000);
    }

    private void ListenLoop()
    {
        var ct = _cts!.Token;

        while (!ct.IsCancellationRequested)
        {
            UdpClient? udp = null;
            try
            {
                udp = new UdpClient();
                udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new IPEndPoint(IPAddress.Any, MulticastPort));
                udp.JoinMulticastGroup(MulticastGroup);
                udp.Client.ReceiveTimeout = 3000;

                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        byte[] data = udp.Receive(ref ep);

                        if (data.Length == 0) continue;

                        var json = Encoding.UTF8.GetString(data);
                        var pkt = JsonSerializer.Deserialize<DiscoveryPacket>(json);

                        if (pkt == null || pkt.Version < 1) continue;
                        if (pkt.Role.Equals("teacher", StringComparison.OrdinalIgnoreCase)) continue;

                        string ip = !string.IsNullOrEmpty(pkt.IpAddress) ? pkt.IpAddress : ep.Address.ToString();
                        int port = pkt.TcpPort > 0 ? pkt.TcpPort : 17420;

                        bool isNew;
                        lock (_lock) { isNew = _knownIps.Add(ip); }

                        if (isNew)
                        {
                            OnStudentDiscovered?.Invoke(ip, port, pkt.Hostname, pkt.RoomId);
                        }
                    }
                    catch (SocketException) { /* timeout — loop again */ }
                    catch (JsonException) { /* malformed packet — ignore */ }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception)
            {
                // Socket error — wait and retry
                try { Task.Delay(2000, ct).Wait(ct); } catch { break; }
            }
            finally
            {
                try { udp?.DropMulticastGroup(MulticastGroup); } catch { }
                udp?.Dispose();
            }
        }
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
