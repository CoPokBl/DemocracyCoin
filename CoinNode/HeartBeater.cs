using System.Diagnostics;
using System.Net;

namespace CoinNode;

public class HeartBeater(ReliableUdp udp) {
    private const int HeartbeatWarningThreshold = 5000;
    private const int HeartbeatPeriod = 5000;
    
    public event Action? OnDegraded;
    public bool Degraded => _peerHeartbeatTimer.ElapsedMilliseconds > HeartbeatWarningThreshold;

    private readonly Stopwatch _peerHeartbeatTimer = new();
    private Thread _beatThread;
    private CancellationTokenSource _cts = new();

    public void Start() {
        _beatThread = new Thread(Beat);
        _beatThread.Start();
        
        udp.OnHeartbeat += RecordPeerHeartbeat;
        _peerHeartbeatTimer.Start();
    }

    public void Stop() {
        _cts.Cancel();
        _beatThread.Join();
    }
    
    public void WaitForContact(int timeout = -1) {
        Stopwatch sw = Stopwatch.StartNew();
        while (!udp.DoesContactExist) {
            if (sw.ElapsedMilliseconds > timeout && timeout != -1) {
                throw new TaskCanceledException("Timeout while waiting for contact");
            }
            Thread.Sleep(100);
        }
        
        Console.WriteLine("HEARTBEATER RECOGNISED CONTACT");
        sw.Stop();
    }
    
    private async void Beat() {
        while (!_cts.IsCancellationRequested) {
            udp.UnsafeSend([69]);
            try {
                await Task.Delay(HeartbeatPeriod, _cts.Token);
            }
            catch (TaskCanceledException) {
                break;
            }
        }
    }
    
    private void RecordPeerHeartbeat() {
        Console.WriteLine("[HEARTBEAT] Received Peer Heartbeat");
        if (_peerHeartbeatTimer.ElapsedMilliseconds > HeartbeatWarningThreshold) {
            OnDegraded?.Invoke();
        }
        _peerHeartbeatTimer.Restart();
    }
    
}