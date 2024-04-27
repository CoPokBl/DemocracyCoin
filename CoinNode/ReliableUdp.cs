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
    private readonly ConcurrentQueue<byte[]> _incoming = new();
    private readonly ConcurrentQueue<PendingSendMessage> _outgoing = new();
    private readonly List<string> _sent = [];
    private readonly IPEndPoint _peer;
    private readonly List<Thread> _threads = new();
    private readonly CancellationTokenSource _cts = new();
    
    private object _sentLock = new();
    public bool DoesContactExist;
    
    private static void Debug(string msg) {
        Console.WriteLine(msg);
    }

    private void MadeContact() {
        if (!DoesContactExist) {
            Debug("Made contact with peer");
            DoesContactExist = true;
        }
    }

    public ReliableUdp(Socket socket, IPEndPoint peer) {
        _socket = socket;
        _peer = peer;
        _socket.ReceiveTimeout = -1;
        
        Thread sendThread = new(ReceivePackets);
        sendThread.Start();
        
        Thread receiveThread = new(SendPackets);
        receiveThread.Start();
        
        _threads.Add(sendThread);
        _threads.Add(receiveThread);
    }

    public void Stop() => Dispose();
    
    public void Dispose() {
        _cts.Cancel();
        foreach (Thread t in _threads) {
            try {
                t.Join(2000);
            }
            catch (TaskCanceledException) {
                // Ignored
            }
        }
        
        _socket.Dispose();
    }

    private void ReceivePackets() {
        while (!_cts.IsCancellationRequested) {
            byte[] outputBuffer = new byte[MaxPacketSize == -1 ? 64_000 : MaxPacketSize];
            int length = _socket.Receive(outputBuffer);
            MadeContact();
            
            Array.Resize(ref outputBuffer, length);

            byte packetType = outputBuffer[0];
            
            if (packetType == 6) {  // Heartbeat
                OnHeartbeat?.Invoke();
                continue;  // Drop it
            }

            // Message packet
            if (_waitingForAck != null && outputBuffer.SequenceEqual(_waitingForAck)) {
                _waitingForAck = null;
                continue;
            }
            
            byte[] checksum = MD5.HashData(outputBuffer);
            _socket.SendTo(checksum, _peer);
            uint checksumInt = BitConverter.ToUInt32(checksum);
            if (_acknowledged == checksumInt) {  // Not new data
                continue;
            }
            _acknowledged = checksumInt;
            
            _incoming.Enqueue(outputBuffer);
        }
    }
    
    private void SendPackets() {
        while (true) {
            if (_outgoing.TryDequeue(out PendingSendMessage? info)) {
                byte[] data = info.Message;
                IPEndPoint endPoint = info.Peer;
                byte[] checksum = MD5.HashData(data);
                
                Debug($"[SEND] Sending packet with checksum (Safe: {info.Safe}, From: {_socket.LocalEndPoint}): " + BitConverter.ToUInt32(checksum) + " to " + endPoint.Address + ":" + endPoint.Port + "...");
                
                while (true) {  // Go until ack
                    _socket.SendTo(data, endPoint);

                    if (!info.Safe) {  // Don't wait for ack
                        break;
                    }
                    
                    Debug("[SEND] Sent, waiting for ack");
                    
                    _waitingForAck = checksum;
                    Stopwatch sw = Stopwatch.StartNew();
                    while (_waitingForAck != null && sw.ElapsedMilliseconds < 1000) {
                        Thread.Yield();
                    }

                    if (_waitingForAck == null) {
                        Debug("[SEND] Ack received");
                        lock (_sentLock) {
                            _sent.Add(info.Id);
                        }
                        break;
                    }

                    Debug("[SEND] Timeout, resending packet");
                }
            }

            Thread.Yield();
        }
    }
    
    /// <summary>
    /// Send function that sends data to an endpoint, this function should be reliable.
    /// The data will be delivered to the endpoint.
    /// </summary>
    /// <param name="data"></param>
    /// <param name="endPoint"></param>
    /// <returns>True if the packet was sent successfully, otherwise false.</returns>
    public bool Send(byte[] data, int timeout = -1) {
        if (MaxPacketSize != -1 && data.Length > MaxPacketSize) {
            throw new ArgumentException("Packet exceeds maximum");
        }
        
        PendingSendMessage msg = new() {
            Message = data,
            Peer = _peer,
            Id = Guid.NewGuid().ToString(),
            Safe = true
        };
        _outgoing.Enqueue(msg);
        
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
    
    public bool UnsafeSend(byte[] data) {
        if (MaxPacketSize != -1 && data.Length > MaxPacketSize) {
            throw new ArgumentException("Packet exceeds maximum");
        }
        
        PendingSendMessage msg = new() {
            Message = data,
            Peer = _peer,
            Id = Guid.NewGuid().ToString(),
            Safe = false
        };
        _outgoing.Enqueue(msg);
        return true;
    }
    
    /// <summary>
    /// Read packet into array
    /// </summary>
    /// <param name="outputBuffer"></param>
    /// <param name="timeout"></param>
    /// <returns>-1 is there was a timeout, otherwise the length of the received packet.</returns>
    public int Receive(byte[] outputBuffer, int timeout = -1) {
        Stopwatch sw = Stopwatch.StartNew();
        while (timeout == -1 || sw.ElapsedMilliseconds < timeout) {
            if (!_incoming.TryDequeue(out byte[] data)) continue;
            Array.Copy(data, outputBuffer, data.Length);
            return data.Length;
        }

        return -1;
    }
    
}