using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SosLan.Services;

public class AlarmMessage
{
    public string InstanceId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
}

/// <summary>
/// Diffuse et reçoit les alertes sur le réseau local via UDP broadcast.
/// </summary>
public class NetworkService : IDisposable
{
    private readonly Guid _instanceId;
    private UdpClient? _listener;
    private CancellationTokenSource? _cts;
    private int _port;

    public event Action<string>? AlarmReceived;

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
    }

    public void Stop()
    {
        _cts?.Cancel();
        _listener?.Close();
        _listener?.Dispose();
        _listener = null;
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

                if (message != null && message.InstanceId != _instanceId.ToString())
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

    public void BroadcastAlarm(string senderName)
    {
        var message = new AlarmMessage
        {
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
