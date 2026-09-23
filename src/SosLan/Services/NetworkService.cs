using System.Collections.Generic;
using System.Linq;
using System.Net;
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

    private readonly Guid _instanceId;
    private readonly object _peersLock = new();
    private readonly Dictionary<string, (string Name, DateTime LastSeen)> _peers = new();

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

                if (message.Type == "alarm")
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
            InstanceId = _instanceId.ToString(),
            SenderName = senderName
        };

        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var sender = new UdpClient();
        sender.EnableBroadcast = true;
        sender.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Broadcast, _port));
    }

    public void Dispose() => Stop();
}
