using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Timer = System.Threading.Timer;

namespace SosLan.Services;

public class AlarmMessage
{
    public string Type { get; set; } = "announce";
    public string MessageId { get; set; } = string.Empty;
    public string InstanceId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
}

/// <summary>
/// Diffuse et reçoit les alertes sur le réseau local via UDP broadcast, et
/// entretient une présence périodique permettant de lister les postes détectés.
/// </summary>
public class NetworkService : IDisposable
{
    private static readonly TimeSpan AnnounceInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan PeerTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan SeenMessageTtl = TimeSpan.FromMinutes(2);

    private readonly Guid _instanceId;
    private readonly object _peersLock = new();
    private readonly Dictionary<string, (string Name, DateTime LastSeen)> _peers = new();
    private readonly object _seenLock = new();
    private readonly Dictionary<string, DateTime> _seenMessageIds = new();

    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private Timer? _announceTimer;
    private int _port;

    public event Action<string>? AlarmReceived;

    public string DisplayName { get; set; } = string.Empty;

    public NetworkService(Guid instanceId)
    {
        _instanceId = instanceId;
    }

    public void Start(int port)
    {
        Stop();

        _port = port;
        _cts = new CancellationTokenSource();
        _listener = new UdpClient
        {
            ExclusiveAddressUse = false
        };
        _listener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _listener.Client.Bind(new IPEndPoint(IPAddress.Any, port));
        _listener.EnableBroadcast = true;

        _ = ListenLoopAsync(_listener, _cts.Token);

        _announceTimer = new Timer(_ => SafeBroadcastAnnounce(), null, TimeSpan.Zero, AnnounceInterval);
    }

    public void Stop()
    {
        _announceTimer?.Dispose();
        _announceTimer = null;

        _cts?.Cancel();
        _listener?.Close();
        _listener?.Dispose();
        _listener = null;

        lock (_peersLock)
        {
            _peers.Clear();
        }
    }

    /// <summary>
    /// Postes actuellement considérés en ligne (une annonce reçue récemment).
    /// </summary>
    public IReadOnlyList<(string Name, TimeSpan LastSeenAgo)> GetActivePeers()
    {
        var now = DateTime.UtcNow;
        var cutoff = now - PeerTimeout;

        lock (_peersLock)
        {
            return _peers.Values
                .Where(p => p.LastSeen >= cutoff)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => (p.Name, now - p.LastSeen))
                .ToList();
        }
    }

    private async Task ListenLoopAsync(UdpClient listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await listener.ReceiveAsync(token);
                var json = Encoding.UTF8.GetString(result.Buffer);
                var message = JsonSerializer.Deserialize<AlarmMessage>(json);

                if (message == null || message.InstanceId == _instanceId.ToString())
                {
                    continue;
                }

                lock (_peersLock)
                {
                    _peers[message.InstanceId] = (message.SenderName, DateTime.UtcNow);
                }

                if (message.Type == "alarm" && !IsDuplicateMessage(message.MessageId))
                {
                    AlarmReceived?.Invoke(message.SenderName);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // Message invalide ignoré.
            }
        }
    }

    public void BroadcastAlarm(string senderName) => Broadcast("alarm", senderName);

    private void SafeBroadcastAnnounce()
    {
        try
        {
            Broadcast("announce", DisplayName);
        }
        catch
        {
            // Réseau temporairement indisponible : ignoré, retenté au prochain tick.
        }
    }

    private void Broadcast(string type, string senderName)
    {
        var message = new AlarmMessage
        {
            Type = type,
            MessageId = Guid.NewGuid().ToString("N"),
            InstanceId = _instanceId.ToString(),
            SenderName = senderName
        };

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        // Le broadcast limité 255.255.255.255 ne sort que par UNE interface (celle de plus
        // basse métrique), ce qui rend invisibles les postes joignables via une autre
        // interface (réseau de VM VMware/Hyper-V, VPN, second adaptateur...). On émet donc
        // sur chaque interface active, vers l'adresse de broadcast de son sous-réseau, avec
        // un socket lié à l'IP de l'interface pour forcer la sortie par celle-ci.
        foreach (var (localAddress, broadcastAddress) in GetBroadcastTargets())
        {
            try
            {
                using var sender = new UdpClient(new IPEndPoint(localAddress, 0));
                sender.EnableBroadcast = true;
                sender.Send(bytes, bytes.Length, new IPEndPoint(broadcastAddress, _port));
            }
            catch
            {
                // Interface qui refuse l'envoi (en cours de déconnexion...) : on passe à la suivante.
            }
        }
    }

    private static List<(IPAddress Local, IPAddress Broadcast)> GetBroadcastTargets()
    {
        var targets = new List<(IPAddress, IPAddress)>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    if (unicast.Address.AddressFamily != AddressFamily.InterNetwork || unicast.PrefixLength is <= 0 or >= 32)
                    {
                        continue;
                    }

                    var ip = unicast.Address.GetAddressBytes();
                    var hostMask = ~(uint.MaxValue << (32 - unicast.PrefixLength));
                    var ipValue = (uint)(ip[0] << 24 | ip[1] << 16 | ip[2] << 8 | ip[3]);
                    var broadcastValue = ipValue | hostMask;
                    var broadcast = new IPAddress(new[]
                    {
                        (byte)(broadcastValue >> 24), (byte)(broadcastValue >> 16),
                        (byte)(broadcastValue >> 8), (byte)broadcastValue
                    });

                    targets.Add((unicast.Address, broadcast));
                }
            }
        }
        catch
        {
            // Énumération des interfaces impossible : repli sur le broadcast limité ci-dessous.
        }

        if (targets.Count == 0)
        {
            targets.Add((IPAddress.Any, IPAddress.Broadcast));
        }

        return targets;
    }

    /// <summary>
    /// Un même message émis sur plusieurs interfaces peut nous parvenir plusieurs fois
    /// (postes multi-interfaces) : on ne déclenche l'alerte qu'une fois par MessageId.
    /// </summary>
    private bool IsDuplicateMessage(string messageId)
    {
        if (string.IsNullOrEmpty(messageId))
        {
            return false;
        }

        var now = DateTime.UtcNow;

        lock (_seenLock)
        {
            foreach (var expired in _seenMessageIds.Where(kv => now - kv.Value > SeenMessageTtl).Select(kv => kv.Key).ToList())
            {
                _seenMessageIds.Remove(expired);
            }

            return !_seenMessageIds.TryAdd(messageId, now);
        }
    }

    public void Dispose() => Stop();
}
