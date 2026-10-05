using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.IO;

namespace AnimeAudioCaptioner.Services;

public sealed class LoopbackBridgeServer : IAsyncDisposable
{
    private const string Prefix = "http://127.0.0.1:38495/";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly WhisperCaptionEngine _engine;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private HttpListener? _listener;
    private CancellationTokenSource? _serverCancellation;
    private Task? _acceptLoop;
    private WebSocket? _socket;

    public bool IsRunning => _listener?.IsListening == true;
    public event EventHandler<string>? StatusChanged;

    public LoopbackBridgeServer(WhisperCaptionEngine engine)
    {
        _engine = engine;
        _engine.TranscriptReady += (_, text) => _ = SendAsync(new BridgeReply("transcript", Text: text));
        _engine.StatusChanged += (_, message) =>
        {
            StatusChanged?.Invoke(this, message);
            _ = SendAsync(new BridgeReply("state", State: "error", Detail: message));
        };
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning) return Task.CompletedTask;
        _serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new HttpListener();
        _listener.Prefixes.Add(Prefix);
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_serverCancellation.Token), _serverCancellation.Token);
        StatusChanged?.Invoke(this, "로컬 오디오 연결 대기 중 (127.0.0.1:38495)");
        return Task.CompletedTask;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
                _ = Task.Run(() => HandleContextAsync(context, cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                StatusChanged?.Invoke(this, $"로컬 연결 오류: {error.Message}");
            }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            context.Response.StatusCode = 200;
            await using var writer = new StreamWriter(context.Response.OutputStream, Encoding.UTF8, leaveOpen: false);
            await writer.WriteAsync("AnimeAudioCaptioner is running");
            return;
        }

        // 외부 네트워크가 아니라 Chrome 확장만 localhost 연결을 열 수 있게 제한한다.
        var origin = context.Request.Headers["Origin"];
        if (string.IsNullOrWhiteSpace(origin) || !origin.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = 403;
            context.Response.Close();
            return;
        }

        var webSocketContext = await context.AcceptWebSocketAsync(subProtocol: null);
        var socket = webSocketContext.WebSocket;
        var replaced = Interlocked.Exchange(ref _socket, socket);
        if (replaced is not null && replaced.State == WebSocketState.Open)
        {
            try { await replaced.CloseAsync(WebSocketCloseStatus.NormalClosure, "새 Chrome 탭 연결", CancellationToken.None); } catch { }
        }

        StatusChanged?.Invoke(this, "Chrome 탭 오디오 연결됨 — 음성을 기다리는 중");
        await SendAsync(new BridgeReply("state", State: "connected", Detail: "EXE 연결됨"), socket, cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var text = await ReceiveTextAsync(socket, cancellationToken);
                if (text is null) break;
                var message = JsonSerializer.Deserialize<BridgeMessage>(text, Json);
                if (message is not null) await HandleBridgeMessageAsync(message, socket, cancellationToken);
            }
        }
        catch (WebSocketException) when (cancellationToken.IsCancellationRequested)
        {
            // 정상 종료
        }
        catch (Exception error)
        {
            StatusChanged?.Invoke(this, $"Chrome 연결 오류: {error.Message}");
        }
        finally
        {
            if (ReferenceEquals(socket, _socket)) Interlocked.CompareExchange(ref _socket, null, socket);
            try { socket.Dispose(); } catch { }
            StatusChanged?.Invoke(this, "Chrome 오디오 연결이 끊겼어요.");
        }
    }

    private async Task HandleBridgeMessageAsync(BridgeMessage message, WebSocket socket, CancellationToken cancellationToken)
    {
        switch (message.Type)
        {
            case "start":
                _engine.Reset(message.Language);
                await SendAsync(new BridgeReply("state", State: "listening", Detail: "탭 오디오를 받는 중"), socket, cancellationToken);
                break;
            case "audio" when !string.IsNullOrWhiteSpace(message.Pcm16):
                try
                {
                    _engine.AppendPcm16(Convert.FromBase64String(message.Pcm16), message.SampleRate, message.Language);
                }
                catch (FormatException)
                {
                    await SendAsync(new BridgeReply("state", State: "error", Detail: "오디오 데이터 형식이 올바르지 않아요."), socket, cancellationToken);
                }
                break;
            case "stop":
                _engine.Reset(null);
                await SendAsync(new BridgeReply("state", State: "stopped", Detail: "오디오 자막을 중지했어요."), socket, cancellationToken);
                break;
            case "ping":
                await SendAsync(new BridgeReply("pong"), socket, cancellationToken);
                break;
        }
    }

    private static async Task<string?> ReceiveTextAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];
        await using var output = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) continue;
            output.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length);
        }
    }

    private async Task SendAsync(BridgeReply message, WebSocket? target = null, CancellationToken cancellationToken = default)
    {
        var socket = target ?? _socket;
        if (socket?.State != WebSocketState.Open) return;
        var payload = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message, Json));
        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            }
        }
        catch (WebSocketException)
        {
            // 탭이 닫히는 중일 수 있다.
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public async Task StopAsync()
    {
        var cancellation = Interlocked.Exchange(ref _serverCancellation, null);
        if (cancellation is null) return;
        cancellation.Cancel();
        var listener = Interlocked.Exchange(ref _listener, null);
        try { listener?.Stop(); } catch { }
        var socket = Interlocked.Exchange(ref _socket, null);
        if (socket?.State == WebSocketState.Open)
        {
            try { await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "프로그램 중지", CancellationToken.None); } catch { }
        }
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop; } catch (OperationCanceledException) { }
        }
        cancellation.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _sendGate.Dispose();
    }
}
