using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CelinesChat.Services.Web.Http;

internal enum HttpMethod
{
    GET,
    POST,
    HEAD,
    PUT,
    DELETE,
    OPTIONS,
    PATCH,
    Other,
}

internal sealed class SourceInfo
{
    public SourceInfo(string ipAddress) => IpAddress = ipAddress;

    public string IpAddress { get; }
}

internal sealed class HeaderCollection : IEnumerable<KeyValuePair<string, string>>
{
    private readonly List<KeyValuePair<string, string>> items = new();

    public void Add(string name, string value) => items.Add(new KeyValuePair<string, string>(name, value));

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class HttpRequest
{
    private readonly Dictionary<string, string> headers;

    public HttpRequest(HttpMethod method, string path, Dictionary<string, string> headers, byte[] body, string remoteIp)
    {
        Method = method;
        Path = path;
        this.headers = headers;
        Body = body;
        Source = new SourceInfo(remoteIp);
    }

    public HttpMethod Method { get; }

    public string Path { get; }

    public byte[] Body { get; }

    public SourceInfo Source { get; }

    public string? DataAsString => Body.Length == 0 ? null : Encoding.UTF8.GetString(Body);

    public string? RetrieveHeaderValue(string key) => headers.TryGetValue(key, out var value) ? value : null;
}

internal sealed class HttpResponse
{
    private static readonly byte[] Crlf = "\r\n"u8.ToArray();

    private readonly Stream stream;
    private bool headersSent;

    public HttpResponse(Stream stream) => this.stream = stream;

    public int StatusCode { get; set; } = 200;

    public string ContentType { get; set; } = "text/plain";

    public HeaderCollection Headers { get; } = new();

    public bool ServerSentEvents { get; set; }

    public bool ResponseSent { get; private set; }

    public async Task Send(string body = "")
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        await WriteHeaders(bytes.LongLength).ConfigureAwait(false);
        if (bytes.Length > 0)
        {
            await stream.WriteAsync(bytes).ConfigureAwait(false);
        }

        await stream.FlushAsync().ConfigureAwait(false);
        ResponseSent = true;
    }

    public async Task<bool> SendChunk(byte[] chunk, bool isFinal, CancellationToken token)
    {
        try
        {
            if (!headersSent)
            {
                await WriteChunkedHeaders().ConfigureAwait(false);
            }

            if (chunk.Length > 0)
            {
                var frameHeader = Encoding.ASCII.GetBytes(chunk.Length.ToString("x") + "\r\n");
                await stream.WriteAsync(frameHeader, token).ConfigureAwait(false);
                await stream.WriteAsync(chunk, token).ConfigureAwait(false);
                await stream.WriteAsync(Crlf, token).ConfigureAwait(false);
            }

            if (isFinal)
            {
                await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), token).ConfigureAwait(false);
                ResponseSent = true;
            }

            await stream.FlushAsync(token).ConfigureAwait(false);
            return true;
        }
        catch (Exception)
        {
            ResponseSent = true;
            return false;
        }
    }

    private Task WriteHeaders(long contentLength)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(StatusCode).Append(' ').Append(ReasonPhrase(StatusCode)).Append("\r\n");
        sb.Append("Content-Type: ").Append(ContentType).Append("\r\n");
        sb.Append("Content-Length: ").Append(contentLength).Append("\r\n");
        AppendCustomHeaders(sb);
        sb.Append("\r\n");
        headersSent = true;
        return stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString())).AsTask();
    }

    private Task WriteChunkedHeaders()
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ").Append(StatusCode).Append(' ').Append(ReasonPhrase(StatusCode)).Append("\r\n");
        sb.Append("Content-Type: ").Append(ServerSentEvents ? "text/event-stream" : ContentType).Append("\r\n");
        sb.Append("Transfer-Encoding: chunked\r\n");
        sb.Append("Cache-Control: no-cache\r\n");
        AppendCustomHeaders(sb);
        sb.Append("\r\n");
        headersSent = true;
        return stream.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString())).AsTask();
    }

    private void AppendCustomHeaders(StringBuilder sb)
    {
        foreach (var header in Headers)
        {
            sb.Append(header.Key).Append(": ").Append(header.Value).Append("\r\n");
        }
    }

    private static string ReasonPhrase(int statusCode) => statusCode switch
    {
        200 => "OK",
        202 => "Accepted",
        400 => "Bad Request",
        401 => "Unauthorized",
        404 => "Not Found",
        429 => "Too Many Requests",
        500 => "Internal Server Error",
        _ => "Unknown",
    };

    internal void SetConnectionHeader(bool keepAlive) => Headers.Add("Connection", keepAlive ? "keep-alive" : "close");
}

internal sealed class HttpContext
{
    public HttpContext(HttpRequest request, HttpResponse response, CancellationToken token)
    {
        Request = request;
        Response = response;
        Token = token;
    }

    public HttpRequest Request { get; }

    public HttpResponse Response { get; }

    public CancellationToken Token { get; }
}

internal sealed class ExceptionEventArgs
{
    public ExceptionEventArgs(HttpContext context, Exception exception)
    {
        Context = context;
        Exception = exception;
    }

    public HttpContext Context { get; }

    public Exception Exception { get; }
}

internal sealed class MiniHttpServerEvents
{
    public event EventHandler<ExceptionEventArgs>? ExceptionEncountered;

    public Action<string>? Logger;

    internal void RaiseException(HttpContext ctx, Exception ex) => ExceptionEncountered?.Invoke(this, new ExceptionEventArgs(ctx, ex));
}

internal sealed class StaticRouteTable
{
    private readonly Dictionary<(HttpMethod Method, string Path), (Func<HttpContext, Task> Handler, Func<HttpContext, Exception, Task>? ExceptionHandler)> routes = new();

    public void Add(HttpMethod method, string path, Func<HttpContext, Task> handler, Func<HttpContext, Exception, Task>? exceptionHandler = null)
        => routes[(method, path)] = (handler, exceptionHandler);

    public bool TryMatch(HttpMethod method, string path, out Func<HttpContext, Task> handler, out Func<HttpContext, Exception, Task>? exceptionHandler)
    {
        if (routes.TryGetValue((method, path), out var entry))
        {
            handler = entry.Handler;
            exceptionHandler = entry.ExceptionHandler;
            return true;
        }

        handler = null!;
        exceptionHandler = null;
        return false;
    }
}

internal sealed class StaticRouteGroup
{
    public StaticRouteTable Static { get; } = new();
}

internal sealed class WebServerRoutes
{
    public StaticRouteGroup PreAuthentication { get; } = new();

    public StaticRouteGroup PostAuthentication { get; } = new();

    public Func<HttpContext, Task>? AuthenticateRequest { get; set; }

    public Func<HttpContext, Task> Default { get; set; } = null!;
}

internal sealed class MiniHttpServer : IDisposable
{
    private const int MaxHeaderBytes = 65_536;
    private const int MaxBodyBytes = 1_048_576;
    private static readonly TimeSpan KeepAliveIdleTimeout = TimeSpan.FromSeconds(30);

    private readonly int port;
    private TcpListener? listener;
    private CancellationTokenSource? cts;
    private Task? acceptLoop;

    public MiniHttpServer(int port, Func<HttpContext, Task> defaultRoute)
    {
        this.port = port;
        Routes.Default = defaultRoute;
    }

    public WebServerRoutes Routes { get; } = new();

    public MiniHttpServerEvents Events { get; } = new();

    public bool IsListening { get; private set; }

    public void Start()
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        IsListening = true;
        cts = new CancellationTokenSource();
        acceptLoop = AcceptLoopAsync(cts.Token);
    }

    public void Stop()
    {
        IsListening = false;
        cts?.Cancel();
        try
        {
            listener?.Stop();
        }
        catch (Exception)
        {
        }
    }

    public void Dispose()
    {
        Stop();
        cts?.Dispose();
        cts = null;
        listener = null;
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }

            _ = HandleClientAsync(client, token);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverToken)
    {
        using (client)
        {
            client.NoDelay = true;
            var stream = client.GetStream();
            string remoteIp;
            try
            {
                remoteIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
            }
            catch (Exception)
            {
                return;
            }

            while (!serverToken.IsCancellationRequested)
            {
                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(serverToken);
                idleCts.CancelAfter(KeepAliveIdleTimeout);

                HttpRequest request;
                try
                {
                    request = await ReadRequestAsync(stream, remoteIp, idleCts.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return;
                }

                var response = new HttpResponse(stream);
                var keepAlive = !string.Equals(request.RetrieveHeaderValue("Connection"), "close", StringComparison.OrdinalIgnoreCase);
                response.SetConnectionHeader(keepAlive);
                var ctx = new HttpContext(request, response, serverToken);

                try
                {
                    await Dispatch(ctx).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Events.RaiseException(ctx, ex);
                    if (!response.ResponseSent)
                    {
                        try
                        {
                            response.StatusCode = 500;
                            await response.Send().ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                        }
                    }
                }

                if (!response.ResponseSent)
                {
                    try
                    {
                        response.StatusCode = 500;
                        await response.Send().ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        return;
                    }
                }

                if (!keepAlive || response.ServerSentEvents || !client.Connected)
                {
                    return;
                }
            }
        }
    }

    private async Task Dispatch(HttpContext ctx)
    {
        if (Routes.PreAuthentication.Static.TryMatch(ctx.Request.Method, ctx.Request.Path, out var preHandler, out var preExceptionHandler))
        {
            await InvokeRoute(ctx, preHandler, preExceptionHandler).ConfigureAwait(false);
            return;
        }

        if (Routes.AuthenticateRequest != null)
        {
            await Routes.AuthenticateRequest(ctx).ConfigureAwait(false);
            if (ctx.Response.ResponseSent)
            {
                return;
            }
        }

        if (Routes.PostAuthentication.Static.TryMatch(ctx.Request.Method, ctx.Request.Path, out var postHandler, out var postExceptionHandler))
        {
            await InvokeRoute(ctx, postHandler, postExceptionHandler).ConfigureAwait(false);
            return;
        }

        await Routes.Default(ctx).ConfigureAwait(false);
    }

    private static async Task InvokeRoute(HttpContext ctx, Func<HttpContext, Task> handler, Func<HttpContext, Exception, Task>? exceptionHandler)
    {
        try
        {
            await handler(ctx).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            if (exceptionHandler == null)
            {
                throw;
            }

            await exceptionHandler(ctx, ex).ConfigureAwait(false);
        }
    }

    private static async Task<HttpRequest> ReadRequestAsync(NetworkStream stream, string remoteIp, CancellationToken token)
    {
        var buffer = new List<byte>(512);
        var readChunk = new byte[4096];
        int terminatorIndex;

        while ((terminatorIndex = IndexOfHeaderTerminator(buffer)) < 0)
        {
            if (buffer.Count > MaxHeaderBytes)
            {
                throw new IOException("Request headers exceeded the maximum allowed size.");
            }

            var read = await stream.ReadAsync(readChunk, token).ConfigureAwait(false);
            if (read == 0)
            {
                if (buffer.Count == 0)
                {
                    throw new EndOfStreamException();
                }

                throw new EndOfStreamException("Connection closed mid-request.");
            }

            for (var i = 0; i < read; i++)
            {
                buffer.Add(readChunk[i]);
            }
        }

        var headerText = Encoding.ASCII.GetString(buffer.GetRange(0, terminatorIndex).ToArray());
        var leftoverBodyBytes = buffer.GetRange(terminatorIndex + 4, buffer.Count - terminatorIndex - 4).ToArray();

        var lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
        {
            throw new IOException("Empty request.");
        }

        var requestLineParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (requestLineParts.Length < 2)
        {
            throw new IOException("Malformed request line.");
        }

        var method = Enum.TryParse<HttpMethod>(requestLineParts[0], ignoreCase: true, out var parsedMethod) ? parsedMethod : HttpMethod.Other;
        var path = requestLineParts[1];
        var queryIndex = path.IndexOf('?');
        if (queryIndex >= 0)
        {
            path = path[..queryIndex];
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < lines.Length; i++)
        {
            var colonIndex = lines[i].IndexOf(':');
            if (colonIndex <= 0)
            {
                continue;
            }

            headers[lines[i][..colonIndex].Trim()] = lines[i][(colonIndex + 1)..].Trim();
        }

        var contentLength = headers.TryGetValue("Content-Length", out var contentLengthValue) && int.TryParse(contentLengthValue, out var parsedLength)
            ? parsedLength
            : 0;

        if (contentLength > MaxBodyBytes)
        {
            throw new IOException("Request body exceeded the maximum allowed size.");
        }

        byte[] body;
        if (contentLength <= 0)
        {
            body = Array.Empty<byte>();
        }
        else if (leftoverBodyBytes.Length >= contentLength)
        {
            body = leftoverBodyBytes[..contentLength];
        }
        else
        {
            body = new byte[contentLength];
            Array.Copy(leftoverBodyBytes, body, leftoverBodyBytes.Length);
            var remaining = contentLength - leftoverBodyBytes.Length;
            var offset = leftoverBodyBytes.Length;
            while (remaining > 0)
            {
                var read = await stream.ReadAsync(body.AsMemory(offset, remaining), token).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new EndOfStreamException("Connection closed while reading the request body.");
                }

                offset += read;
                remaining -= read;
            }
        }

        return new HttpRequest(method, path, headers, body, remoteIp);
    }

    private static int IndexOfHeaderTerminator(List<byte> buffer)
    {
        if (buffer.Count < 4)
        {
            return -1;
        }

        for (var i = 0; i <= buffer.Count - 4; i++)
        {
            if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n' && buffer[i + 2] == (byte)'\r' && buffer[i + 3] == (byte)'\n')
            {
                return i;
            }
        }

        return -1;
    }
}
