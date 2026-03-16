namespace EdgeTtsSharp;

using System;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

internal class WebSocketClientWrapper
{
    private readonly CancellationToken CancelToken;
    private readonly Func<ValueTask> OnClose;
    private readonly Func<Exception, ValueTask>? OnError;
    private readonly Func<WebSocketClientWrapper, byte[], ValueTask> OnMessage;
    private readonly Uri Uri;
    private readonly ClientWebSocket WebSocket;

    public WebSocketClientWrapper(string url,
        Func<WebSocketClientWrapper, byte[], ValueTask> onMessage,
        Func<ValueTask> onClose,
        Func<Exception, ValueTask>? onError = null,
        IWebProxy? proxy = null,
        CancellationToken ct = default)
    {
        this.Uri = new Uri(url);
        this.WebSocket = new ClientWebSocket
        {
            Options =
            {
#if NETSTANDARD2_1
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
#endif
            },
        };

        this.WebSocket.Options.Proxy = proxy;
        this.WebSocket.Options.Cookies = new CookieContainer();


        this.OnMessage = onMessage;
        this.OnClose = onClose;
        this.OnError = onError;
        this.CancelToken = ct;
    }

    public void AddHeader(string key, string value)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        // 专门处理：子协议（所有版本均支持，确保一定生效）
        if (string.Equals(key, "Sec-WebSocket-Protocol", StringComparison.OrdinalIgnoreCase))
        {
            // 支持逗号分隔多个子协议
            var protocols = (value ?? "").Split(new[] { ',', }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < protocols.Length; i++)
            {
                var p = protocols[i].Trim();
                if (p.Length > 0)
                {
                    this.WebSocket.Options.AddSubProtocol(p);
                }
            }

            return;
        }

        var methodInfo = this.WebSocket.Options.GetType().GetMethod("SetRequestHeader");
        if (methodInfo != null)
        {
            methodInfo.Invoke(this.WebSocket.Options, [key, value,]);
        }
    }

    public async ValueTask Close()
    {
        try
        {
            await this.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", this.CancelToken);
        }
        catch
        {
            // ignore
        }

        await this.OnClose();
    }


    public async ValueTask Connect()
    {
        try
        {
            await this.WebSocket.ConnectAsync(this.Uri, this.CancelToken);

            if (this.WebSocket.State != WebSocketState.Open)
            {
                throw new Exception("Failed to connect to the server.");
            }

            // Start receiving messages
            this.ReceiveMessages(this.CancelToken).GetAwaiter();
        }
        catch (Exception e)
        {
            this.OnError?.Invoke(e);
            throw;
        }
    }

    public async ValueTask Send(string msg)
    {
        var messageBuffer = Encoding.UTF8.GetBytes(msg);
#if NETSTANDARD2_1
            await this.WebSocket.SendAsync(messageBuffer.AsMemory(), WebSocketMessageType.Text, true, this.CancelToken);
#else
        await this.WebSocket.SendAsync(new ArraySegment<byte>(messageBuffer), WebSocketMessageType.Text, true, this.CancelToken);
#endif
    }

    private async ValueTask ReceiveMessages(CancellationToken ct)
    {
        try
        {
            var buffer = new byte[1024 * 4];
            while (this.WebSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
#if NETSTANDARD2_1
                    var result = await this.WebSocket.ReceiveAsync(buffer.AsMemory(), this.CancelToken);
#else
                var result = await this.WebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), this.CancelToken);
#endif
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await this.Close();
                }
                else
                {
#if NETSTANDARD2_1
                        var rawData = buffer.AsMemory()[..result.Count].ToArray();
#else
                    var rawData = new ArraySegment<byte>(buffer, 0, result.Count).ToArray();
#endif
                    await this.OnMessage(this, rawData);
                }
            }
        }
        catch (Exception e)
        {
            var onError = this.OnError;
            if (onError != null)
            {
                await onError(e);
            }
        }
    }
}