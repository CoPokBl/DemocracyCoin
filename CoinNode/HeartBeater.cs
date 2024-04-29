using System.Diagnostics;

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
        if (_cts.IsCancellationRequested) {
            return;  // We are already stopped, no point complaining
        }
        _cts.Cancel();
        _beatThread.Join();
    }
    
    public void WaitForContact(int timeout = -1) {
        if (!udp.ContactSwitch.Wait(timeout)) {
            throw new TaskCanceledException("Timeout while waiting for contact");
        }
        Logger.Debug("HEART", "HEARTBEATER RECOGNISED CONTACT");
    }
    
    private async void Beat() {
        while (!_cts.IsCancellationRequested) {
            udp.UnsafeSend([6]);
            try {
                await Task.Delay(HeartbeatPeriod, _cts.Token);
            }
            catch (TaskCanceledException) {
                break;
            }
        }
    }
    
    private void RecordPeerHeartbeat() {
        if (_peerHeartbeatTimer.ElapsedMilliseconds > HeartbeatWarningThreshold) {
            OnDegraded?.Invoke();
        }
        _peerHeartbeatTimer.Restart();
    }
    
}