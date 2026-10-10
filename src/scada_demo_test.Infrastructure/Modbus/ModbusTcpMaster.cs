using System.Net.Sockets;

namespace scada_demo_test.Infrastructure.Modbus;

// Minimal, dependency-free Modbus TCP master client (RFC-standard Modbus/TCP with
// MBAP header). The gateways (Norvi ESP32 / USR-W610) forward the request out over
// RS-485 to the attached slave meters, so the platform only ever speaks TCP.
//
// One ModbusTcpSession is opened per device per polling cycle: every sensor attached
// to that gateway is read over the same connection, then the session is disposed.
public class ModbusTcpMaster
{
    public async Task<ModbusTcpSession> OpenAsync(string ipAddress, int port, int timeoutMs, CancellationToken ct)
    {
        var client = new TcpClient();

        var connectTask = client.ConnectAsync(ipAddress, port);
        var timeoutTask = Task.Delay(timeoutMs, ct);
        if (await Task.WhenAny(connectTask, timeoutTask) != connectTask)
        {
            client.Dispose();
            ct.ThrowIfCancellationRequested(); // report shutdown as cancellation, not timeout
            throw new ModbusConnectException($"Gateway {ipAddress}:{port} did not accept the connection within {timeoutMs} ms.");
        }

        try
        {
            await connectTask; // surfaces any immediate connect failure
        }
        catch
        {
            client.Dispose();
            throw;
        }

        client.ReceiveTimeout = timeoutMs;
        client.SendTimeout = timeoutMs;

        return new ModbusTcpSession(client, timeoutMs);
    }
}

// A TCP-level connect failure (the gateway refused or never accepted the socket
// within the connect budget). Distinct from ModbusException so the scanner can
// treat it as a gateway-wide fault rather than a silent slave address.
public sealed class ModbusRepeatedResponseException(byte slaveId, byte function, ushort start,
    ushort quantity, byte[] firstPayload, byte[] repeatedPayload)
    : ModbusException($"Slave {slaveId} returned more than one response to the same transaction.", isProtocolError: true)
{
    public byte SlaveId { get; } = slaveId;
    public IReadOnlyList<ScadaEngine.Core.Models.RawScanResponse> Responses { get; } =
        new[] { firstPayload, repeatedPayload }.Select(payload => new ScadaEngine.Core.Models.RawScanResponse
        {
            SlaveId = slaveId, FunctionCode = function, StartRegister = start, RegisterQuantity = quantity,
            Payload = (byte[])payload.Clone(), IsSuccess = true
        }).ToArray();
}

public class ModbusConnectException : Exception
{
    public ModbusConnectException(string message) : base(message) { }
    public ModbusConnectException(string message, Exception? inner) : base(message, inner) { }
}

// One persistent connection to a single gateway. Requests share the stream and a
// monotonic transaction-ID counter; receive latency is bounded by TimeoutMs.
public sealed class ModbusTcpSession : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly int _timeoutMs;
    private static int _transactionCounter;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private ushort _lastTxId;
    private (byte Slave, byte Function, ushort Start, ushort Quantity, byte[] Payload)? _lastResponse;

    internal ModbusTcpSession(TcpClient client, int timeoutMs)
    {
        _client = client;
        _stream = client.GetStream();
        _timeoutMs = timeoutMs;
    }

    public Task<byte[]> ReadHoldingRegistersAsync(
        byte slaveId,
        ushort startRegister,
        ushort registerQuantity,
        CancellationToken ct,
        int? readTimeoutMs = null)
        => ReadRegistersAsync(0x03, slaveId, startRegister, registerQuantity, ct, readTimeoutMs);

    public Task<byte[]> ReadInputRegistersAsync(
        byte slaveId,
        ushort startRegister,
        ushort registerQuantity,
        CancellationToken ct,
        int? readTimeoutMs = null)
        => ReadRegistersAsync(0x04, slaveId, startRegister, registerQuantity, ct, readTimeoutMs);

    private async Task<byte[]> ReadRegistersAsync(
        byte responseFuncCode,
        byte slaveId,
        ushort startRegister,
        ushort registerQuantity,
        CancellationToken ct,
        int? readTimeoutMs = null)
    {
        if (slaveId is < 1 or > 247) throw new ArgumentOutOfRangeException(nameof(slaveId));
        if (registerQuantity is < 1 or > 125 || (int)startRegister + registerQuantity > 65536)
            throw new ArgumentOutOfRangeException(nameof(registerQuantity));

        var timeoutMs = readTimeoutMs is > 0 ? readTimeoutMs.Value : _timeoutMs;
        await _requestGate.WaitAsync(ct);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(timeoutMs);
        try
        {
            await RejectRepeatedResponseAsync(timeoutMs, deadline.Token);
            var request = BuildReadFrame(responseFuncCode, slaveId, startRegister, registerQuantity);
            await _stream.WriteAsync(request, deadline.Token);
            var header = await ReadExactlyAsync(7, deadline.Token, timeoutMs);
            var responseTx = (ushort)((header[0] << 8) | header[1]);
            var length = (header[4] << 8) | header[5];
            if (responseTx != _lastTxId || header[2] != 0 || header[3] != 0 ||
                header[6] != slaveId || length is < 3 or > 254)
                throw new ModbusException("Invalid Modbus MBAP transaction, protocol, unit or length.", isProtocolError: true);

            var pdu = await ReadExactlyAsync(length - 1, deadline.Token, timeoutMs);
            if (pdu[0] == (responseFuncCode | 0x80) && pdu.Length == 2)
                throw new ModbusException($"Slave {slaveId} returned Modbus exception code 0x{pdu[1]:X2}.",
                    exceptionCode: pdu[1]);
            if (pdu[0] != responseFuncCode || pdu.Length != 2 + registerQuantity * 2 ||
                pdu[1] != registerQuantity * 2)
                throw new ModbusException("Invalid Modbus function or register byte count.", isProtocolError: true);

            var payload = pdu[2..];
            _lastResponse = (slaveId, responseFuncCode, startRegister, registerQuantity, payload);
            await RejectRepeatedResponseAsync(timeoutMs, deadline.Token);
            return payload;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // The per-request deadline fired (read timeout), NOT a real scan/shutdown
            // cancellation. Surface it as TimeoutException so callers (scanner probe,
            // polling worker) treat it as "this address/window is silent" instead of
            // aborting the whole sweep as SCAN_CANCELLED.
            //
            // The session is DISPOSED here: after a timed-out request the bridge may
            // deliver the late response at any moment, and those stray bytes would be
            // misread as the next transaction's reply. A timed-out session is dead -
            // the next read must fail loudly (ObjectDisposedException) so callers
            // reconnect instead of parsing garbage.
            KillSession();
            throw new TimeoutException($"Gateway did not respond within {timeoutMs} ms.");
        }
        catch (TimeoutException)
        {
            // ReadExactlyAsync turns its own deadline into a TimeoutException - it must
            // kill the session for exactly the same reason as the branch above, or the
            // late reply of THIS request would be parsed as the answer to the NEXT one.
            KillSession();
            throw;
        }
        catch (OperationCanceledException)
        {
            // Caller cancellation also abandons an in-flight transaction. Its late
            // reply must never be consumed by a later request on this connection.
            KillSession();
            throw;
        }
        catch (ModbusException ex) when (ex.IsProtocolError)
        {
            // An invalid MBAP header may leave an unread PDU (or more replies) behind.
            KillSession();
            throw;
        }
        catch (IOException)
        {
            KillSession();
            throw;
        }
        catch
        {
            _lastResponse = null;
            throw;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    // A timed-out session is dead: any byte still in flight belongs to the abandoned
    // transaction and would desynchronise every later read on this connection.
    private void KillSession()
    {
        _lastResponse = null;
        try { _stream.Dispose(); _client.Dispose(); } catch { }
    }

    private async Task RejectRepeatedResponseAsync(int timeoutMs, CancellationToken ct)
    {
        if (_lastResponse is not { } last || !_stream.DataAvailable) return;
        try
        {
            var header = await ReadExactlyAsync(7, ct, timeoutMs);
            var length = (header[4] << 8) | header[5];
            if (((header[0] << 8) | header[1]) != _lastTxId || header[2] != 0 || header[3] != 0 ||
                header[6] != last.Slave || length is < 3 or > 254)
            {
                throw new ModbusException("Invalid trailing Modbus MBAP response.", isProtocolError: true);
            }

            // The header belongs to our transaction, so the PDU MUST be drained even
            // when its shape differs: leaving it in the stream would feed those bytes
            // to the next request's header read and turn a real bus conflict into a
            // meaningless framing error.
            var pdu = await ReadExactlyAsync(length - 1, ct, timeoutMs);
            bool looksLikeAnAnswer = pdu.Length > 0 &&
                (pdu[0] == last.Function || pdu[0] == (last.Function | 0x80));
            if (!looksLikeAnAnswer)
            {
                throw new ModbusException("Invalid trailing Modbus function.", isProtocolError: true);
            }

            // Thrown on purpose: two answers to one transaction mean two devices are
            // racing on this slave ID - regardless of whether they reply with the same
            // shape (identical meters) or one replies with data and the other with a
            // Modbus exception (different families sharing the ID). The outer catch
            // below must NOT swallow it.
            var payload = pdu.Length > 2 && pdu[0] == last.Function && pdu[1] == last.Payload.Length
                ? pdu[2..]
                : Array.Empty<byte>();
            throw new ModbusRepeatedResponseException(last.Slave, last.Function, last.Start, last.Quantity,
                last.Payload, payload);
        }
        catch (ModbusRepeatedResponseException)
        {
            _lastResponse = null;
            throw;
        }
        catch
        {
            _lastResponse = null;
            // A partial trailing frame, cancellation or broken stream is not a
            // successful read. Let the request handler invalidate the connection.
            throw;
        }
    }

    private byte[] BuildReadFrame(byte func, byte slaveId, ushort startRegister, ushort registerQuantity)
    {
        var tx = (ushort)Interlocked.Increment(ref _transactionCounter);
        _lastTxId = tx;

        var frame = new byte[12];
        frame[0] = (byte)(tx >> 8);      // Transaction ID (high)
        frame[1] = (byte)(tx & 0xFF);    // Transaction ID (low)
        frame[2] = 0x00;                 // Protocol ID
        frame[3] = 0x00;                 // Protocol ID
        frame[4] = 0x00;                 // Length (high)
        frame[5] = 0x06;                 // Length = 6 (unit + func + 2x address + 2x quantity)
        frame[6] = slaveId;              // Unit / Slave ID
        frame[7] = func;                 // Function code: 0x03 Read Holding / 0x04 Read Input
        frame[8] = (byte)(startRegister >> 8);
        frame[9] = (byte)(startRegister & 0xFF);
        frame[10] = (byte)(registerQuantity >> 8);
        frame[11] = (byte)(registerQuantity & 0xFF);
        return frame;
    }

    private async Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct, int timeoutMs)
    {
        var buffer = new byte[count];
        int offset = 0;

        // Linked CTS drives a REAL hard read deadline (CancelAfter), because async
        // reads ignore TcpClient.ReceiveTimeout. A silent serial bridge would
        // otherwise hang the probe forever.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        while (offset < count)
        {
            int read;
            try
            {
                read = await _stream.ReadAsync(buffer.AsMemory(offset, count - offset), cts.Token);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Gateway did not respond within {timeoutMs} ms.", ex);
            }

            if (read == 0)
            {
                throw new IOException("Gateway closed the connection before the full response arrived.");
            }
            offset += read;
        }

        return buffer;
    }

    public void Dispose()
    {
        _stream.Dispose();
        _client.Dispose();
    }
}

// Raised for any protocol-level Modbus failure (exception codes, bad unit/function,
// transaction mismatch) so the polling worker can tag the sensor OFFLINE gracefully.
public class ModbusException : Exception
{
    // Numeric Modbus exception code when the failure was an exception-frame reply
    // (e.g. 0x0B = gateway target device failed to respond). Null for framing errors.
    public byte? ExceptionCode { get; }

    // True when the reply was not a valid response to OUR request: unit ID mismatch,
    // transaction mismatch, wrong function code or wrong byte count. On a serial
    // bridge an empty address replays another device's cached frame, so this means
    // "this address does not serve this window" - the scanner maps it to WindowInvalid
    // and CONTINUES (it is NOT a bus-wide fault).
    public bool IsProtocolError { get; }

    public ModbusException(string message) : base(message) { }
    public ModbusException(string message, Exception? inner = null, byte? exceptionCode = null, bool isProtocolError = false)
        : base(message, inner) { ExceptionCode = exceptionCode; IsProtocolError = isProtocolError; }
}
