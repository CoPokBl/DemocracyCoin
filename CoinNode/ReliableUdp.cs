using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace CoinNode;

public class ReliableUdp : IDisposable {
    public event Action? OnHeartbeat;
    public int MaxPacketSize = -1;
    
    private readonly Socket _socket;
    private uint _acknowledged = 1;
    private byte[]? _waitingForAck;
    private readonly BlockingCollection<byte[]> _incoming = new();
    private readonly BlockingCollection<PendingSendMessage> _outgoing = new();
    private readonly List<string> _sent = [];
    public readonly IPEndPoint Peer;
    private readonly List<Thread> _threads = [];
    private readonly CancellationTokenSource _cts = new();
    
    private readonly object _sentLock = new();
    public readonly ManualResetEventSlim ContactSwitch = new(false);
    
    private static void Debug(string msg) {
        Logger.Debug("SEND", msg);
    }

    private void MadeContact() {
        if (!ContactSwitch.IsSet) {
            Debug("Made contact with peer");
            ContactSwitch.Set();
        }
    }

    public ReliableUdp(Socket socket, IPEndPoint peer) {
        _socket = socket;
        Peer = peer;
        _socket.ReceiveTimeout = -1;

        Thread r = new(ReceivePackets);
        r.Start();
        _threads.Add(r);

        Thread s = new(SendPackets);
        s.Start();
        _threads.Add(s);
    }

    public void Stop(bool inclSocket = true) => Dispose(inclSocket);

    public void Dispose() => Dispose(true);

    public void Dispose(bool inclSocket) {
        _cts.Cancel();
        foreach (Thread t in _threads) {
            try {
                t.Join(2000);
            }
            catch (TaskCanceledException) {
                // Ignored
            }
        }
        
        if (inclSocket) _socket.Dispose();
    }

    private async void ReceivePackets() {
        while (!_cts.IsCancellationRequested) {
            byte[] outputBuffer = new byte[MaxPacketSize == -1 ? 64_000 : MaxPacketSize];
            int length;
            try {
                length = await _socket.ReceiveAsync(outputBuffer);
            }
            catch (Exception e) {
                Logger.Debug("NODE", e.ToString());
                continue;
            }
            MadeContact();
            
            Array.Resize(ref outputBuffer, length);

            byte packetType = outputBuffer[0];
            
            if (packetType == 6) {  // Heartbeat
                OnHeartbeat?.Invoke();
                continue;  // Drop it
            }

            if (packetType == 7) {  // ack
                if (_waitingForAck != null && outputBuffer.SequenceEqual(_waitingForAck)) {
                    _waitingForAck = null;
                    continue;
                }
                
                Logger.Debug("NET", "Received ack for packet we aren't waiting for");
                continue;
            }

            // Packet to pass onto a receiver
            byte[] checksum = MD5.HashData(outputBuffer);
            _socket.SendTo(new byte[] {7}.Concat(checksum).ToArray(), Peer);
            uint checksumInt = BitConverter.ToUInt32(checksum);
            if (_acknowledged == checksumInt) {  // Not new data
                continue;
            }
            _acknowledged = checksumInt;
            
            _incoming.Add(outputBuffer);
        }
    }
    
    private async void SendPackets() {
        while (true) {
            if (_outgoing.TryTake(out PendingSendMessage? info)) {
                byte[] data = info.Message;
                IPEndPoint endPoint = info.Peer;
                byte[] checksum = MD5.HashData(data);
                
                Debug($"Sending packet with checksum (Safe: {info.Safe}, From: {_socket.LocalEndPoint}): " + BitConverter.ToUInt32(checksum) + " to " + endPoint.Address + ":" + endPoint.Port + "...");
                
                while (true) {  // Go until ack
                    await _socket.SendToAsync(data, endPoint);

                    if (!info.Safe) {  // Don't wait for ack
                        break;
                    }
                    
                    Debug("Sent, waiting for ack");
                    
                    _waitingForAck = new byte[] {7}.Concat(checksum).ToArray();
                    Stopwatch sw = Stopwatch.StartNew();
                    while (_waitingForAck != null && sw.ElapsedMilliseconds < 1000) {
                        Thread.Yield();
                    }

                    if (_waitingForAck == null) {
                        Debug("Ack received");
                        lock (_sentLock) {
                            _sent.Add(info.Id);
                        }
                        break;
                    }

                    Debug("Timeout, resending packet");
                }
            }
        }
        // ReSharper disable once FunctionNeverReturns
    }

    /// <summary>
    /// Send function that sends data to an endpoint, this function should be reliable.
    /// The data will be delivered to the endpoint.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="timeout"></param>
    /// <returns>True if the packet was sent successfully, otherwise false.</returns>
    public bool Send(byte[] data, int timeout = -1) {
        if (MaxPacketSize != -1 && data.Length > MaxPacketSize) {
            throw new ArgumentException("Packet exceeds maximum");
        }
        
        PendingSendMessage msg = new() {
            Message = data,
            Peer = Peer,
            Id = Guid.NewGuid().ToString(),
            Safe = true
        };
        _outgoing.Add(msg);
        
        Stopwatch sw = Stopwatch.StartNew();
        while (timeout == -1 || sw.ElapsedMilliseconds < timeout) {
            lock (_sentLock) {
                if (_sent.Contains(msg.Id)) {
                    return true;
                }
            }
            
            Thread.Yield();
        }

        return false;
    }
    
    public async Task UnsafeSend(byte[] data) {
        if (MaxPacketSize != -1 && data.Length > MaxPacketSize) {
            throw new ArgumentException("Packet exceeds maximum");
        }

        await _socket.SendToAsync(data, Peer);
    }
    
    /// <summary>
    /// Read packet into array
    /// </summary>
    /// <param name="outputBuffer"></param>
    /// <param name="timeout"></param>
    /// <returns>-1 is there was a timeout, otherwise the length of the received packet.</returns>
    public int Receive(byte[] outputBuffer, int timeout = -1) {
        if (!_incoming.TryTake(out byte[]? data, timeout)) {
            return -1;
        }

        System.Diagnostics.Debug.Assert(data.Length <= MaxPacketSize || MaxPacketSize == -1, $"Packet exceeds max size, max: {MaxPacketSize}");
        Array.Copy(data, outputBuffer, data.Length);
        return data.Length;
    }
    
}