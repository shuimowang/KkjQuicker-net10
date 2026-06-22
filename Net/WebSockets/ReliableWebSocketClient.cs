using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KkjQuicker.Net.WebSockets
{
    /// <summary>表示 WebSocket 客户端配置。</summary>
    public sealed class WebSocketClientOptions
    {
        /// <summary>服务器地址（ws:// 或 wss://）。</summary>
        public string Url { get; set; }

        /// <summary>自定义 HTTP 请求头，常用于传递 Token / API Key。</summary>
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>Keep-Alive 间隔，Zero 表示禁用。默认 30 秒。</summary>
        public TimeSpan KeepAliveInterval { get; set; }

        /// <summary>单次接收缓冲区大小（字节）。默认 8192。</summary>
        public int ReceiveBufferSize { get; set; }

        /// <summary>单条完整消息允许的最大字节数，0 表示不限制。默认 16 MB。</summary>
        public int MaxMessageBytes { get; set; }

        /// <summary>连接超时，Zero 表示不限制。默认 15 秒。</summary>
        public TimeSpan ConnectTimeout { get; set; }

        /// <summary>自动重连配置，null 表示禁用。</summary>
        public WebSocketReconnectOptions Reconnect { get; set; }

        public WebSocketClientOptions()
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30);
            ReceiveBufferSize = 8192;
            MaxMessageBytes = 16 * 1024 * 1024;
            ConnectTimeout = TimeSpan.FromSeconds(15);
        }
    }

    /// <summary>表示 WebSocket 自动重连配置。</summary>
    public sealed class WebSocketReconnectOptions
    {
        /// <summary>是否启用自动重连。</summary>
        public bool Enabled { get; set; }

        /// <summary>最大重试次数。-1 表示无限重试。</summary>
        public int MaxRetries { get; set; }

        /// <summary>首次重连前的等待时长。</summary>
        public TimeSpan InitialDelay { get; set; }

        /// <summary>指数退避时的最大等待上限。Zero 表示不限制。</summary>
        public TimeSpan MaxDelay { get; set; }

        /// <summary>
        /// 是否使用指数退避策略；
        /// <see langword="false"/> 时固定使用 <see cref="InitialDelay"/>。
        /// </summary>
        public bool UseExponentialBackoff { get; set; }

        public WebSocketReconnectOptions()
        {
            Enabled = true;
            MaxRetries = 5;
            InitialDelay = TimeSpan.FromSeconds(1);
            MaxDelay = TimeSpan.FromSeconds(30);
            UseExponentialBackoff = true;
        }
    }

    /// <summary>
    /// 提供带接收循环与可选自动重连能力的 WebSocket 客户端。
    /// </summary>
    /// <remarks>
    /// <para>Dispose 立即取消接收循环并释放底层资源，不发送 Close 帧。</para>
    /// <para>如需优雅关闭，请先调用 DisconnectAsync。</para>
    /// <para>重连期间请勿并发调用 ConnectAsync；内部已通过 _isReconnecting 守卫防止误用。</para>
    /// </remarks>
    public sealed class ReliableWebSocketClient : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private readonly Uri _uri;
        private readonly WebSocketClientOptions _options;

        private ClientWebSocket _socket;
        private CancellationTokenSource _receiveCts;
        private CancellationTokenSource _reconnectCts;
        private Task _receiveTask;

        private volatile bool _disposed;
        private volatile bool _manualDisconnect;
        private volatile bool _isReconnecting;

        private int _reconnectAttempts;

        /// <summary>收到完整文本消息时发生。</summary>
        public event EventHandler<string> TextMessageReceived;
        /// <summary>收到完整二进制消息时发生。</summary>
        public event EventHandler<byte[]> BinaryMessageReceived;
        /// <summary>连接状态文本变化时发生。</summary>
        public event EventHandler<string> StateChanged;
        /// <summary>发生错误时发生。</summary>
        public event EventHandler<Exception> Error;
        /// <summary>连接断开时发生。</summary>
        public event EventHandler Disconnected;

        /// <summary>当前 WebSocket 状态。</summary>
        public WebSocketState State
        {
            get { lock (_syncRoot) return _socket?.State ?? WebSocketState.None; }
        }

        /// <summary>当前是否已连接。</summary>
        public bool IsConnected => State == WebSocketState.Open;

        public ReliableWebSocketClient(WebSocketClientOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.Url))
                throw new ArgumentException("Url 不能为空白。", nameof(options));
            if (options.ReceiveBufferSize <= 0)
                throw new ArgumentOutOfRangeException(
                    "options.ReceiveBufferSize", "必须大于 0。");
            if (options.MaxMessageBytes < 0)
                throw new ArgumentOutOfRangeException(
                    "options.MaxMessageBytes", "不能小于 0。");

            if (options.Reconnect != null)
            {
                var r = options.Reconnect;
                if (r.MaxRetries < -1)
                    throw new ArgumentOutOfRangeException(
                        "options.Reconnect.MaxRetries", "不能小于 -1。");
                if (r.InitialDelay < TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(
                        "options.Reconnect.InitialDelay", "不能为负数。");
                if (r.MaxDelay < TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(
                        "options.Reconnect.MaxDelay", "不能为负数。");
                if (r.MaxDelay > TimeSpan.Zero && r.InitialDelay > r.MaxDelay)
                    throw new ArgumentException(
                        "InitialDelay 不能大于 MaxDelay。", nameof(options));
            }

            _options = options;
            _uri = new Uri(options.Url, UriKind.Absolute);
        }

        public ReliableWebSocketClient(string url, Dictionary<string, string> headers = null)
            : this(new WebSocketClientOptions { Url = url, Headers = headers }) { }

        // ── 公共 API ──────────────────────────────────────────────────────────

        /// <summary>连接到服务器。</summary>
        /// <exception cref="InvalidOperationException">
        /// 正在自动重连、已连接或接收循环未结束时抛出。
        /// </exception>
        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (_isReconnecting)
                    throw new InvalidOperationException(
                        "正在自动重连中，请勿手动调用 ConnectAsync。");
            }

            return ConnectCoreAsync(cancellationToken);
        }

        /// <summary>优雅断开（发送 Close 帧并等待接收循环退出）。</summary>
        public async Task DisconnectAsync()
        {
            if (_disposed) return;

            ClientWebSocket socket;
            CancellationTokenSource receiveCts;
            Task receiveTask;

            lock (_syncRoot)
            {
                _manualDisconnect = true;
                _reconnectCts?.Cancel();

                socket = _socket;
                receiveCts = _receiveCts;
                receiveTask = _receiveTask;

                if (socket == null) return;
            }

            try
            {
                if (socket.State == WebSocketState.Open ||
                    socket.State == WebSocketState.CloseReceived)
                {
                    try
                    {
                        using (var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
                            await socket.CloseAsync(
                                WebSocketCloseStatus.NormalClosure,
                                "Client closed",
                                closeCts.Token).ConfigureAwait(false);
                    }
                    catch { /* 优雅关闭失败时忽略，继续清理 */ }
                }

                receiveCts?.Cancel();

                if (receiveTask != null)
                {
                    try { await receiveTask.ConfigureAwait(false); }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { OnError(ex); }
                }
            }
            finally
            {
                lock (_syncRoot)
                    CleanupSocket_NoLock();

                OnStateChanged("WebSocket 已断开");
                OnDisconnected();
            }
        }

        /// <summary>发送文本消息。</summary>
        public async Task SendTextAsync(
            string text, CancellationToken cancellationToken = default)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            await SendAsyncCore(
                Encoding.UTF8.GetBytes(text),
                WebSocketMessageType.Text,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>发送二进制消息。</summary>
        public async Task SendBinaryAsync(
            byte[] data, CancellationToken cancellationToken = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            await SendAsyncCore(data, WebSocketMessageType.Binary, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>立即释放资源，不发送 Close 帧。</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _manualDisconnect = true;

            lock (_syncRoot)
            {
                _reconnectCts?.Cancel();
                _receiveCts?.Cancel();
                CleanupSocket_NoLock();
            }

            _sendLock.Dispose();
        }

        // ── 私有：连接 ────────────────────────────────────────────────────────

        private async Task ConnectCoreAsync(CancellationToken cancellationToken)
        {
            ClientWebSocket socket;
            CancellationTokenSource receiveCts;

            lock (_syncRoot)
            {
                if (_socket != null &&
                    (_socket.State == WebSocketState.Open ||
                     _socket.State == WebSocketState.Connecting))
                    throw new InvalidOperationException("WebSocket 已连接或正在连接。");

                if (_receiveTask != null && !_receiveTask.IsCompleted)
                    throw new InvalidOperationException("上一次接收循环尚未结束，请先断开连接。");

                _manualDisconnect = false;
                CleanupSocket_NoLock();

                socket = new ClientWebSocket();

                if (_options.KeepAliveInterval > TimeSpan.Zero)
                    socket.Options.KeepAliveInterval = _options.KeepAliveInterval;

                if (_options.Headers != null)
                    foreach (var pair in _options.Headers)
                        socket.Options.SetRequestHeader(pair.Key, pair.Value);

                receiveCts = new CancellationTokenSource();
                _socket = socket;
                _receiveCts = receiveCts;
                _receiveTask = null;
            }

            try
            {
                OnStateChanged("正在连接...");
                await ConnectWithTimeoutAsync(socket, cancellationToken).ConfigureAwait(false);

                lock (_syncRoot)
                {
                    _reconnectAttempts = 0;
                    _receiveTask = Task.Run(() => ReceiveLoopAsync(socket, receiveCts.Token));
                }

                OnStateChanged("WebSocket 已连接");
            }
            catch (Exception ex)
            {
                lock (_syncRoot)
                {
                    if (ReferenceEquals(_socket, socket))
                        CleanupSocket_NoLock();
                }
                OnError(ex);
                throw;
            }
        }

        private async Task ConnectWithTimeoutAsync(
            ClientWebSocket socket, CancellationToken externalToken)
        {
            var timeout = _options.ConnectTimeout;
            if (timeout <= TimeSpan.Zero)
            {
                await socket.ConnectAsync(_uri, externalToken).ConfigureAwait(false);
                return;
            }

            using (var timeoutCts = new CancellationTokenSource(timeout))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(
                externalToken, timeoutCts.Token))
            {
                try
                {
                    await socket.ConnectAsync(_uri, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (timeoutCts.IsCancellationRequested &&
                          !externalToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"连接超时（{timeout.TotalSeconds:0.0} 秒）。");
                }
            }
        }

        // ── 私有：发送 ────────────────────────────────────────────────────────

        private async Task SendAsyncCore(
            byte[] data,
            WebSocketMessageType messageType,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            ClientWebSocket socket;
            lock (_syncRoot)
            {
                if (_socket == null || _socket.State != WebSocketState.Open)
                    throw new InvalidOperationException("WebSocket 未连接。");
                socket = _socket;
            }

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(
                    new ArraySegment<byte>(data),
                    messageType,
                    true,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        // ── 私有：接收循环 ────────────────────────────────────────────────────

        private async Task ReceiveLoopAsync(
            ClientWebSocket socket,
            CancellationToken cancellationToken)
        {
            var buffer = new byte[_options.ReceiveBufferSize];
            var messageBuffer = new MemoryStream(_options.ReceiveBufferSize);

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    WebSocketReceiveResult result;
                    try
                    {
                        result = await socket.ReceiveAsync(
                            new ArraySegment<byte>(buffer),
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }
                    catch (WebSocketException ex) { OnError(ex); break; }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        OnStateChanged("服务器关闭了连接");
                        break;
                    }

                    if (result.Count > 0)
                        messageBuffer.Write(buffer, 0, result.Count);

                    if (_options.MaxMessageBytes > 0 &&
                        messageBuffer.Length > _options.MaxMessageBytes)
                    {
                        OnError(new InvalidDataException(string.Format(
                            "WebSocket 消息超过最大限制（{0} 字节）。",
                            _options.MaxMessageBytes)));
                        break;
                    }

                    if (!result.EndOfMessage)
                        continue;

                    // GetBuffer() 返回内部共享缓冲区，避免 ToArray() 的额外分配
                    var raw = messageBuffer.GetBuffer();
                    var length = (int)messageBuffer.Length;
                    messageBuffer.SetLength(0);

                    try
                    {
                        if (result.MessageType == WebSocketMessageType.Text)
                        {
                            OnTextMessageReceived(Encoding.UTF8.GetString(raw, 0, length));
                        }
                        else if (result.MessageType == WebSocketMessageType.Binary)
                        {
                            // Binary 须复制：共享缓冲区下次写入会覆盖内容
                            var copy = new byte[length];
                            Buffer.BlockCopy(raw, 0, copy, 0, length);
                            OnBinaryMessageReceived(copy);
                        }
                    }
                    catch (Exception ex)
                    {
                        // 隔离订阅者异常，不影响接收循环
                        OnError(ex);
                    }
                }
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
            finally
            {
                messageBuffer.Dispose();
                OnStateChanged("接收循环已退出");

                bool shouldReconnect;
                lock (_syncRoot)
                {
                    _receiveTask = null;
                    shouldReconnect = !_manualDisconnect && !_disposed;
                }

                if (shouldReconnect)
                    await HandleUnexpectedDisconnectAsync().ConfigureAwait(false);
            }
        }

        // ── 私有：重连 ────────────────────────────────────────────────────────

        private async Task HandleUnexpectedDisconnectAsync()
        {
            var reconnect = _options.Reconnect;
            if (reconnect == null || !reconnect.Enabled)
            {
                lock (_syncRoot) CleanupSocket_NoLock();
                OnDisconnected();
                return;
            }

            _isReconnecting = true;

            CancellationTokenSource reconnectCts;
            lock (_syncRoot)
            {
                reconnectCts = new CancellationTokenSource();
                _reconnectCts = reconnectCts;
            }

            try
            {
                while (!_manualDisconnect && !_disposed)
                {
                    if (reconnect.MaxRetries >= 0 &&
                        _reconnectAttempts >= reconnect.MaxRetries)
                    {
                        OnStateChanged("重连终止：已达到最大重试次数。");
                        lock (_syncRoot) CleanupSocket_NoLock();
                        OnDisconnected();
                        return;
                    }

                    _reconnectAttempts++;

                    var delay = GetReconnectDelay(reconnect, _reconnectAttempts);
                    OnStateChanged(string.Format(
                        "正在进行第 {0} 次重连，延迟 {1:0.0} 秒。",
                        _reconnectAttempts, delay.TotalSeconds));

                    lock (_syncRoot) CleanupSocket_NoLock();

                    try
                    {
                        await Task.Delay(delay, reconnectCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { break; }

                    if (_manualDisconnect || _disposed) break;

                    try
                    {
                        await ConnectCoreAsync(CancellationToken.None).ConfigureAwait(false);
                        OnStateChanged("重连成功");
                        return;
                    }
                    catch (OperationCanceledException) { break; }
                    catch (Exception ex) { OnError(ex); }
                }
            }
            finally
            {
                _isReconnecting = false;

                lock (_syncRoot)
                {
                    if (ReferenceEquals(_reconnectCts, reconnectCts))
                        _reconnectCts = null;
                }
                reconnectCts.Dispose();
            }

            lock (_syncRoot) CleanupSocket_NoLock();
            OnDisconnected();
        }

        // ── 私有：辅助 ────────────────────────────────────────────────────────

        private static TimeSpan GetReconnectDelay(WebSocketReconnectOptions options, int attempt)
        {
            if (!options.UseExponentialBackoff)
                return options.InitialDelay;

            var initialMs = options.InitialDelay.TotalMilliseconds;
            var maxMs = options.MaxDelay.TotalMilliseconds <= 0
                ? initialMs
                : options.MaxDelay.TotalMilliseconds;

            var value = Math.Min(initialMs * Math.Pow(2, attempt - 1), maxMs);

            // ±20% 随机抖动，防止多客户端同时断线时产生惊群效应。
            double jitter = (Random.Shared.NextDouble() * 2.0 - 1.0) * (value * 0.2);

            return TimeSpan.FromMilliseconds(Math.Max(value + jitter, 0));
        }

        /// <summary>释放 socket 与 receiveCts，调用方须持有 _syncRoot 锁。</summary>
        private void CleanupSocket_NoLock()
        {
            _receiveCts?.Dispose();
            _receiveCts = null;

            _socket?.Dispose();
            _socket = null;

            _receiveTask = null;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ReliableWebSocketClient));
        }

        private void OnTextMessageReceived(string text) =>
            SafeRaise(TextMessageReceived, text);

        private void OnBinaryMessageReceived(byte[] data) =>
            SafeRaise(BinaryMessageReceived, data);

        private void OnStateChanged(string message) =>
            SafeRaise(StateChanged, message);

        private void OnError(Exception ex)
        {
            var handler = Error;
            if (handler == null)
                return;

            foreach (EventHandler<Exception> single in handler.GetInvocationList())
            {
                try
                {
                    single(this, ex);
                }
                catch (Exception handlerException)
                {
                    Trace.TraceError("ReliableWebSocketClient Error handler failed: {0}", handlerException);
                }
            }
        }

        private void OnDisconnected() =>
            SafeRaise(Disconnected, EventArgs.Empty);

        private void SafeRaise(EventHandler handler, EventArgs args)
        {
            if (handler == null)
                return;

            foreach (EventHandler single in handler.GetInvocationList())
            {
                try
                {
                    single(this, args);
                }
                catch (Exception ex)
                {
                    OnError(ex);
                }
            }
        }

        private void SafeRaise<T>(EventHandler<T> handler, T args)
        {
            if (handler == null)
                return;

            foreach (EventHandler<T> single in handler.GetInvocationList())
            {
                try
                {
                    single(this, args);
                }
                catch (Exception ex)
                {
                    OnError(ex);
                }
            }
        }
    }
}
