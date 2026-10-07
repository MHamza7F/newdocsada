using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using ScadaEngine.Core.Models;

namespace ScadaEngine.Tests;

internal sealed class FakeGateway : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();
    private readonly ConcurrentBag<Task> _clients = new();
    private readonly Task _accept;
    private readonly Func<byte[], byte[]?> _reply;
    public ConcurrentBag<byte[]> Requests { get; } = new();
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public FakeGateway(Func<byte[], byte[]?> reply)
    {
        _reply = reply;
        _listener.Start();
        _accept = AcceptAsync();
    }

    public FakeGateway(params RawScanResponse[] meters) : this(request =>
    {
        var matching = meters.Where(m => m.SlaveId == request[6]).ToArray();
        var start = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(8));
        var quantity = BinaryPrimitives.ReadUInt16BigEndian(request.AsSpan(10));
        var sample = matching.SelectMany(m => m.Evidence).FirstOrDefault(e =>
            e.Window.FunctionCode == request[7] && e.Window.StartRegister == start && e.Window.RegisterQuantity == quantity);
        return sample is null ? ExceptionFrame(request, matching.Length == 0 ? (byte)11 : (byte)2) : DataFrame(request, sample.Payload);
    }) { }

    public static byte[] ExceptionFrame(byte[] request, byte code) =>
        new[] { request[0], request[1], (byte)0, (byte)0, (byte)0, (byte)3, request[6], (byte)(request[7] | 128), code };

    public static byte[] DataFrame(byte[] request, byte[] payload) =>
        new[] { request[0], request[1], (byte)0, (byte)0, (byte)0, (byte)(3 + payload.Length), request[6], request[7], (byte)payload.Length }
            .Concat(payload).ToArray();

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stop.IsCancellationRequested)
            {
                var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                _clients.Add(ServeAsync(client));
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var request = new byte[12];
                    await stream.ReadExactlyAsync(request, _stop.Token);
                    Requests.Add(request);
                    var reply = _reply(request);
                    if (reply is not null) await stream.WriteAsync(reply, _stop.Token);
                }
            }
            catch (Exception e) when (e is IOException or SocketException or OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _accept;
        await Task.WhenAll(_clients);
        _listener.Stop();
        _stop.Dispose();
    }
}
