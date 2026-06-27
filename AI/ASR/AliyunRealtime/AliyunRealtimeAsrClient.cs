using KkjQuicker.Audio.Abstractions;
using KkjQuicker.Net.WebSockets;
using NAudio.Wave;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace KkjQuicker.AI.ASR.AliyunRealtime
{
    /// <summary>
    /// 阿里实时语音识别会话配置。
    /// </summary>
    /// <remarks>
    /// <para>本配置用于生成 <c>session.update</c> 事件。</para>
    /// <para>
    /// 默认配置面向中文实时语音识别场景：
    /// 16kHz PCM、开启服务端 VAD、自动启动音频采集。
    /// </para>
    /// </remarks>
    public sealed class AliyunRealtimeAsrOptions
    {
        private string? _language = "zh";
        private double _vadThreshold = 0.0;
        private int _silenceDurationMs = 400;
        private int _maxQueuedAudioFrames = 200;
        private int _finishWaitTimeoutMs = 5000;

        /// <summary>
        /// 获取或设置识别语种。
        /// </summary>
        /// <remarks>
        /// 常见值如：<c>zh</c>、<c>en</c>。为空时表示不显式指定语种。
        /// </remarks>
        public string? Language
        {
            get => _language;
            set { _language = string.IsNullOrWhiteSpace(value) ? null : value; }
        }

        /// <summary>
        /// 获取或设置是否启用服务端 VAD。
        /// </summary>
        /// <remarks>
        /// 启用后，服务端可根据静音自动判断分段。
        /// 关闭后，通常需要在停止时显式发送 <c>input_audio_buffer.commit</c>。
        /// </remarks>
        public bool EnableServerVad { get; set; }

        /// <summary>
        /// 获取或设置服务端 VAD 阈值。
        /// </summary>
        /// <remarks>
        /// 仅在 <see cref="EnableServerVad"/> 为 <see langword="true"/> 时生效。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">值不能小于 0。</exception>
        public double VadThreshold
        {
            get => _vadThreshold;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(value), value, "VadThreshold 不能小于 0。");
                _vadThreshold = value;
            }
        }

        /// <summary>
        /// 获取或设置服务端 VAD 判定静音时长（毫秒）。
        /// </summary>
        /// <remarks>
        /// 仅在 <see cref="EnableServerVad"/> 为 <see langword="true"/> 时生效。
        /// 推荐值为 400。较低值响应更快，但更容易在自然停顿处断句。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">值必须大于 0。</exception>
        public int SilenceDurationMs
        {
            get => _silenceDurationMs;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(value), value, "SilenceDurationMs 必须大于 0。");
                _silenceDurationMs = value;
            }
        }

        /// <summary>
        /// 获取或设置收到 <c>session.updated</c> 后是否自动启动音频采集。
        /// </summary>
        public bool AutoStartAudio { get; set; }

        /// <summary>
        /// 获取或设置音频发送队列中最多缓存的帧数。
        /// </summary>
        /// <remarks>
        /// 当网络发送速度低于采集速度时，本值可避免内存无限增长。
        /// 队列满时会丢弃新的音频帧，以保持实时性。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">值必须大于 0。</exception>
        public int MaxQueuedAudioFrames
        {
            get => _maxQueuedAudioFrames;
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(value), value, "MaxQueuedAudioFrames 必须大于 0。");
                _maxQueuedAudioFrames = value;
            }
        }

        /// <summary>
        /// 获取或设置发送停止请求后等待服务端结束消息的最长时间（毫秒）。
        /// </summary>
        /// <remarks>
        /// 超时后会主动断开 WebSocket，避免服务端未返回 session.finished 时客户端停在旧连接上。
        /// 设为 0 可禁用该兜底断开。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">值不能小于 0。</exception>
        public int FinishWaitTimeoutMs
        {
            get => _finishWaitTimeoutMs;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(
                        nameof(value), value, "FinishWaitTimeoutMs 不能小于 0。");
                _finishWaitTimeoutMs = value;
            }
        }

        /// <summary>初始化默认配置。</summary>
        public AliyunRealtimeAsrOptions()
        {
            EnableServerVad = true;
            AutoStartAudio = true;
        }
    }

    /// <summary>
    /// 阿里百炼 / DashScope 实时语音识别客户端。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类型封装阿里实时 ASR WebSocket 协议，建立在
    /// <see cref="ReliableWebSocketClient"/> 与 <see cref="IAudioFrameSource"/> 之上。
    /// </para>
    /// <para>典型流程：</para>
    /// <list type="number">
    /// <item><description>连接 WebSocket。</description></item>
    /// <item><description>发送 <c>session.update</c> 配置会话。</description></item>
    /// <item><description>收到 <c>session.updated</c> 后开始采集并发送 PCM 音频。</description></item>
    /// <item><description>服务端持续返回中间文本与最终文本。</description></item>
    /// <item><description>停止时发送 <c>session.finish</c>，等待服务端返回 <c>session.finished</c>。</description></item>
    /// </list>
    /// <para>
    /// 本类型适合"持续实时识别"场景，例如：麦克风实时转写、系统声音字幕、会议记录。
    /// </para>
    /// <para>
    /// 本类型本身不依赖 WPF，可用于桌面应用、控制台程序、托盘工具或其他宿主环境。
    /// </para>
    /// </remarks>
    public sealed class AliyunRealtimeAsrClient : IDisposable
    {
        private readonly object _syncRoot = new object();
        private readonly ReliableWebSocketClient _webSocket;
        private readonly IAudioFrameSource _audioSource;
        private readonly AliyunRealtimeAsrOptions _options;

        private int _disposed;
        private bool _isRunning;
        private bool _isStopping;
        private bool _audioStarted;
        private bool _sessionUpdateSent;
        private bool _sessionUpdated;
        private BlockingCollection<byte[]>? _audioSendQueue;
        private CancellationTokenSource? _audioSendCts;
        private Task? _audioSendTask;
        private int _audioQueueOverflowNotified;

        /// <summary>
        /// 当收到实时中间文本时发生。
        /// </summary>
        /// <remarks>
        /// 该事件用于显示当前识别中的预览文本，适合绑定到实时字幕或状态区域。
        /// 中间文本可能被后续结果修正，不建议直接作为最终输出持久化。
        /// </remarks>
        public event EventHandler<string>? PartialTranscriptReceived;

        /// <summary>
        /// 当收到最终识别文本时发生。
        /// </summary>
        /// <remarks>
        /// 该事件表示当前语音分段已经完成识别，适合追加到最终文本结果中。
        /// </remarks>
        public event EventHandler<string>? FinalTranscriptReceived;

        /// <summary>
        /// 当状态文本发生变化时发生。
        /// </summary>
        /// <remarks>
        /// 该事件主要用于日志、调试或 UI 提示，不建议将其作为严格的状态机信号来源。
        /// </remarks>
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// 当收到原始服务端文本消息时发生。
        /// </summary>
        /// <remarks>
        /// 适合用于协议调试、日志记录或观察服务端返回结构。
        /// 正常业务逻辑通常不需要依赖该事件。
        /// </remarks>
        public event EventHandler<string>? RawMessageReceived;

        /// <summary>当发生错误时发生。</summary>
        public event EventHandler<Exception>? ErrorOccurred;

        /// <summary>当服务端返回 <c>session.finished</c> 时发生。</summary>
        public event EventHandler? SessionFinished;

        /// <summary>
        /// 获取当前是否处于运行状态。
        /// </summary>
        /// <remarks>
        /// 返回 <see langword="true"/> 表示本次识别会话已启动，
        /// 不保证服务端一定已完成会话配置。
        /// </remarks>
        public bool IsRunning
        {
            get { lock (_syncRoot) return _isRunning; }
        }

        /// <summary>获取当前 WebSocket 是否已连接。</summary>
        public bool IsConnected => _webSocket.IsConnected;

        /// <summary>
        /// 初始化新的客户端实例。
        /// </summary>
        /// <param name="webSocket">底层 WebSocket 客户端。</param>
        /// <param name="audioSource">音频帧来源。</param>
        /// <param name="options">实时识别配置。</param>
        /// <exception cref="ArgumentNullException">参数为 <see langword="null"/> 时抛出。</exception>
        /// <exception cref="ArgumentException">音频格式不符合要求时抛出。</exception>
        public AliyunRealtimeAsrClient(
            ReliableWebSocketClient webSocket,
            IAudioFrameSource audioSource,
            AliyunRealtimeAsrOptions? options = null)
        {
            ArgumentNullException.ThrowIfNull(webSocket);
            ArgumentNullException.ThrowIfNull(audioSource);
            if (audioSource.OutputFormat == null)
                throw new ArgumentException(
                    "音频源必须提供有效的 OutputFormat。", nameof(audioSource));
            if (audioSource.OutputFormat.Encoding != WaveFormatEncoding.Pcm)
                throw new ArgumentException(
                    "阿里实时 ASR 当前要求输入 PCM 音频。", nameof(audioSource));

            _webSocket = webSocket;
            _audioSource = audioSource;
            _options = CloneOptions(options);

            _webSocket.TextMessageReceived += OnWebSocketTextMessageReceived;
            _webSocket.StateChanged += OnWebSocketStateChanged;
            _webSocket.Error += OnWebSocketError;
            _webSocket.Disconnected += OnWebSocketDisconnected;

            _audioSource.ErrorOccurred += OnAudioSourceError;
        }

        private static AliyunRealtimeAsrOptions CloneOptions(AliyunRealtimeAsrOptions? options)
        {
            if (options == null)
                return new AliyunRealtimeAsrOptions();

            return new AliyunRealtimeAsrOptions
            {
                Language = options.Language,
                EnableServerVad = options.EnableServerVad,
                VadThreshold = options.VadThreshold,
                SilenceDurationMs = options.SilenceDurationMs,
                AutoStartAudio = options.AutoStartAudio,
                MaxQueuedAudioFrames = options.MaxQueuedAudioFrames,
                FinishWaitTimeoutMs = options.FinishWaitTimeoutMs
            };
        }

        /// <summary>
        /// 启动识别会话。
        /// </summary>
        /// <remarks>
        /// 本方法会连接 WebSocket，并立即发送一次 <c>session.update</c>。
        /// 若配置了 <see cref="AliyunRealtimeAsrOptions.AutoStartAudio"/>，
        /// 则在收到服务端 <c>session.updated</c> 后自动开始采集音频。
        /// </remarks>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task StartAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (_isRunning)
                    return;

                _isRunning = true;
                _isStopping = false;
                _audioStarted = false;
                _sessionUpdateSent = false;
                _sessionUpdated = false;
            }

            try
            {
                RaiseStatus("连接中...");
                await _webSocket.ConnectAsync(cancellationToken).ConfigureAwait(false);

                RaiseStatus("已连接，正在发送会话配置...");
                await SendSessionUpdateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_syncRoot)
                {
                    _isRunning = false;
                    _isStopping = false;
                    _audioStarted = false;
                    _sessionUpdateSent = false;
                    _sessionUpdated = false;
                }
                throw;
            }
        }

        /// <summary>
        /// 停止识别会话。
        /// </summary>
        /// <remarks>
        /// <para>本方法会先停止音频采集，再根据配置发送协议结束事件。</para>
        /// <para>
        /// 若未启用服务端 VAD，则会先发送 <c>input_audio_buffer.commit</c>，
        /// 然后发送 <c>session.finish</c>。
        /// </para>
        /// <para>本方法仅负责向服务端发起停止请求，不等待 <c>session.finished</c>。</para>
        /// </remarks>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task StopAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (IsDisposed)
                return;

            lock (_syncRoot)
            {
                if (!_isRunning || _isStopping)
                    return;

                _isStopping = true;
            }

            try
            {
                StopAudioCapture();
                await DrainAudioSendQueueAsync(cancellationToken).ConfigureAwait(false);

                if (_webSocket.IsConnected)
                {
                    if (!_options.EnableServerVad)
                        await SendCommitAsync(cancellationToken).ConfigureAwait(false);

                    await SendSessionFinishAsync(cancellationToken).ConfigureAwait(false);
                    lock (_syncRoot)
                        ResetSessionState_NoLock();
                    ScheduleDisconnectAfterStopTimeout();
                }
                else
                {
                    lock (_syncRoot)
                        ResetSessionState_NoLock();
                }
            }
            catch (Exception ex)
            {
                lock (_syncRoot)
                    ResetSessionState_NoLock();

                CancelAudioSendWorker();
                RaiseError(ex);
            }
        }

        /// <summary>发送一次 <c>session.update</c>。</summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task SendSessionUpdateAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();

            lock (_syncRoot)
            {
                if (_sessionUpdateSent)
                    return;
                _sessionUpdateSent = true;
            }

            try
            {
                var payload = new
                {
                    event_id = CreateEventId(),
                    type = "session.update",
                    session = BuildSessionObject()
                };

                await SendJsonAsync(payload, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                lock (_syncRoot)
                    _sessionUpdateSent = false;
                throw;
            }
        }

        /// <summary>发送一次 <c>input_audio_buffer.append</c>。</summary>
        /// <param name="pcm16">PCM 音频数据。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public async Task AppendAudioAsync(
            byte[] pcm16,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();

            ArgumentNullException.ThrowIfNull(pcm16);
            if (pcm16.Length == 0)
                return;

            var payload = new
            {
                event_id = CreateEventId(),
                type = "input_audio_buffer.append",
                audio = Convert.ToBase64String(pcm16)
            };

            await SendJsonAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>发送一次 <c>input_audio_buffer.commit</c>。</summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task SendCommitAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();

            var payload = new
            {
                event_id = CreateEventId(),
                type = "input_audio_buffer.commit"
            };

            return SendJsonAsync(payload, cancellationToken);
        }

        /// <summary>发送一次 <c>session.finish</c>。</summary>
        /// <param name="cancellationToken">取消令牌。</param>
        public Task SendSessionFinishAsync(
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ThrowIfDisposed();

            var payload = new
            {
                event_id = CreateEventId(),
                type = "session.finish"
            };

            return SendJsonAsync(payload, cancellationToken);
        }

        /// <summary>释放当前实例。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _audioSource.ErrorOccurred -= OnAudioSourceError;

            _webSocket.TextMessageReceived -= OnWebSocketTextMessageReceived;
            _webSocket.StateChanged -= OnWebSocketStateChanged;
            _webSocket.Error -= OnWebSocketError;
            _webSocket.Disconnected -= OnWebSocketDisconnected;

            try
            {
                // StopAudioCapture 内部也会做 FrameReady -= ，幂等安全。
                StopAudioCapture();
            }
            catch
            {
            }

            CancelAudioSendWorker();
            _webSocket.Dispose();
        }

        // ── 私有：构建会话对象 ────────────────────────────────────────────────

        private object BuildSessionObject()
        {
            var format = _audioSource.OutputFormat;

            object? turnDetection = null;
            if (_options.EnableServerVad)
            {
                turnDetection = new
                {
                    type = "server_vad",
                    threshold = _options.VadThreshold,
                    silence_duration_ms = _options.SilenceDurationMs
                };
            }

            object? inputAudioTranscription = null;
            if (!string.IsNullOrWhiteSpace(_options.Language))
            {
                inputAudioTranscription = new
                {
                    language = _options.Language
                };
            }

            return new
            {
                modalities = new[] { "text" },
                input_audio_format = "pcm",
                sample_rate = format.SampleRate,
                input_audio_transcription = inputAudioTranscription,
                turn_detection = turnDetection
            };
        }

        // ── 私有：事件处理 ────────────────────────────────────────────────────

        private void OnAudioFrameReady(object? sender, byte[] pcm)
        {
            if (pcm == null || pcm.Length == 0)
                return;

            bool canSend;
            BlockingCollection<byte[]>? queue;
            lock (_syncRoot)
            {
                canSend = _isRunning && !_isStopping && _sessionUpdated;
                queue = _audioSendQueue;
            }

            if (!canSend || queue == null || queue.IsAddingCompleted)
                return;

            var copy = new byte[pcm.Length];
            Buffer.BlockCopy(pcm, 0, copy, 0, pcm.Length);

            try
            {
                if (!queue.TryAdd(copy))
                {
                    if (Interlocked.Exchange(ref _audioQueueOverflowNotified, 1) == 0)
                        RaiseStatus("音频发送队列已满，正在丢弃新的音频帧。");
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void OnWebSocketTextMessageReceived(object? sender, string text)
        {
            RaiseRawMessage(text);

            try
            {
                var obj = JObject.Parse(text);
                var type = ReadString(obj, "type");

                if (string.IsNullOrEmpty(type))
                    return;

                if (type == "session.created")
                {
                    RaiseStatus("服务器已创建会话。");
                    return;
                }

                if (type == "session.updated")
                {
                    lock (_syncRoot)
                        _sessionUpdated = true;

                    RaiseStatus("会话配置成功。");

                    if (_options.AutoStartAudio)
                        StartAudioCapture();

                    return;
                }

                if (type == "conversation.item.input_audio_transcription.text")
                {
                    var textPart = ReadString(obj, "text") ?? string.Empty;
                    var stashPart = ReadString(obj, "stash") ?? string.Empty;
                    var preview = textPart + stashPart;

                    if (!string.IsNullOrEmpty(preview))
                        RaisePartialTranscript(preview);

                    return;
                }

                if (type == "conversation.item.input_audio_transcription.completed")
                {
                    var finalText = ReadString(obj, "transcript");

                    if (!string.IsNullOrEmpty(finalText))
                        RaiseFinalTranscript(finalText);

                    return;
                }

                if (type == "session.finished")
                {
                    StopAudioCapture();
                    CancelAudioSendWorker();

                    lock (_syncRoot)
                        ResetSessionState_NoLock();

                    RaiseStatus("识别结束。");
                    RaiseSessionFinished();

                    // 事件回调为同步上下文，断开逻辑放到后台任务中执行。
                    Task.Run(() => DisconnectAfterFinishAsync());
                    return;
                }

                if (type == "error")
                {
                    var message =
                        ReadString(obj, "error", "message") ??
                        ReadString(obj, "message") ??
                        text;

                    RaiseError(new InvalidOperationException(message));
                    return;
                }
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }
        }

        private async Task DisconnectAfterFinishAsync()
        {
            try
            {
                StopAudioCapture();

                if (_webSocket.IsConnected)
                    await _webSocket.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }
        }

        private void ScheduleDisconnectAfterStopTimeout()
        {
            var timeoutMs = _options.FinishWaitTimeoutMs;
            if (timeoutMs <= 0)
                return;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(timeoutMs).ConfigureAwait(false);

                    bool shouldDisconnect;
                    lock (_syncRoot)
                        shouldDisconnect = !IsDisposed && !_isRunning && !_isStopping;

                    if (shouldDisconnect && _webSocket.IsConnected)
                        await _webSocket.DisconnectAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                }
            });
        }

        private void OnWebSocketStateChanged(object? sender, string message) =>
            RaiseStatus(message);

        private void OnWebSocketError(object? sender, Exception ex) =>
            RaiseError(ex);

        private void OnWebSocketDisconnected(object? sender, EventArgs e)
        {
            StopAudioCapture();
            CancelAudioSendWorker();

            lock (_syncRoot)
                ResetSessionState_NoLock();

            RaiseStatus("WebSocket 已断开。");
        }

        private void OnAudioSourceError(object? sender, Exception ex) =>
            RaiseError(ex);

        // ── 私有：音频采集控制 ────────────────────────────────────────────────

        private void StartAudioCapture()
        {
            lock (_syncRoot)
            {
                if (_audioStarted || _isStopping || !_isRunning)
                    return;

                StartAudioSendWorker_NoLock();
                _audioSource.FrameReady += OnAudioFrameReady;
                _audioStarted = true;
            }

            try
            {
                _audioSource.Start();
                RaiseStatus("录音中...");
            }
            catch
            {
                lock (_syncRoot)
                {
                    if (_audioStarted)
                    {
                        _audioSource.FrameReady -= OnAudioFrameReady;
                        _audioStarted = false;
                    }
                }

                CancelAudioSendWorker();

                try
                {
                    _audioSource.Stop();
                }
                catch
                {
                }

                throw;
            }
        }

        private void StopAudioCapture()
        {
            lock (_syncRoot)
            {
                if (!_audioStarted)
                    return;

                _audioSource.FrameReady -= OnAudioFrameReady;
                _audioStarted = false;
            }

            try
            {
                _audioSource.Stop();
            }
            catch (Exception ex)
            {
                RaiseError(ex);
            }
        }

        private void StartAudioSendWorker_NoLock()
        {
            if (_audioSendTask != null && !_audioSendTask.IsCompleted)
                return;

            _audioQueueOverflowNotified = 0;
            _audioSendQueue = new BlockingCollection<byte[]>(
                new ConcurrentQueue<byte[]>(), _options.MaxQueuedAudioFrames);
            _audioSendCts = new CancellationTokenSource();

            var queue = _audioSendQueue;
            var token = _audioSendCts.Token;
            _audioSendTask = Task.Run(() => ProcessAudioSendQueueAsync(queue, token));
        }

        private async Task DrainAudioSendQueueAsync(CancellationToken cancellationToken)
        {
            BlockingCollection<byte[]>? queue;
            CancellationTokenSource? cts;
            Task? task;

            lock (_syncRoot)
            {
                queue = _audioSendQueue;
                cts = _audioSendCts;
                task = _audioSendTask;

                if (queue == null || task == null)
                    return;

                queue.CompleteAdding();
            }

            using (cancellationToken.Register(() =>
            {
                if (cts != null)
                    cts.Cancel();
            }))
            {
                try
                {
                    await task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            lock (_syncRoot)
            {
                if (ReferenceEquals(_audioSendQueue, queue))
                    _audioSendQueue = null;
                if (ReferenceEquals(_audioSendCts, cts))
                    _audioSendCts = null;
                if (ReferenceEquals(_audioSendTask, task))
                    _audioSendTask = null;
            }

            queue.Dispose();
            if (cts != null)
                cts.Dispose();
        }

        private void CancelAudioSendWorker()
        {
            BlockingCollection<byte[]>? queue;
            CancellationTokenSource? cts;
            Task? task;

            lock (_syncRoot)
            {
                queue = _audioSendQueue;
                cts = _audioSendCts;
                task = _audioSendTask;

                _audioSendQueue = null;
                _audioSendCts = null;
                _audioSendTask = null;
            }

            if (queue != null)
            {
                try { queue.CompleteAdding(); }
                catch (InvalidOperationException) { }
            }

            if (cts != null)
                cts.Cancel();

            if (task != null)
            {
                task.ContinueWith(t =>
                {
                    if (queue != null)
                        queue.Dispose();
                    if (cts != null)
                        cts.Dispose();
                }, TaskScheduler.Default);
            }
            else
            {
                if (queue != null)
                    queue.Dispose();
                if (cts != null)
                    cts.Dispose();
            }
        }

        private async Task ProcessAudioSendQueueAsync(
            BlockingCollection<byte[]> queue,
            CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                byte[] pcm;
                try
                {
                    pcm = queue.Take(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                    break;
                }

                try
                {
                    if (CanSendQueuedAudio())
                        await AppendAudioAsync(pcm, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                }
            }
        }

        private bool CanSendQueuedAudio()
        {
            lock (_syncRoot)
                return !IsDisposed && _isRunning && _sessionUpdated && _webSocket.IsConnected;
        }

        private void ResetSessionState_NoLock()
        {
            _isRunning = false;
            _isStopping = false;
            _audioStarted = false;
            _sessionUpdateSent = false;
            _sessionUpdated = false;
            _audioQueueOverflowNotified = 0;
        }

        // ── 私有：辅助 ────────────────────────────────────────────────────────

        private Task SendJsonAsync(object value, CancellationToken cancellationToken)
        {
            var json = JsonConvert.SerializeObject(value, Formatting.None);
            return _webSocket.SendTextAsync(json, cancellationToken);
        }

        private static string? ReadString(JToken token, params string[] path)
        {
            if (token == null || path == null || path.Length == 0)
                return null;

            JToken? current = token;
            for (int i = 0; i < path.Length; i++)
            {
                if (current == null)
                    return null;
                current = current[path[i]];
            }

            return current?.ToString();
        }

        private static string CreateEventId() =>
            "event_" + Guid.NewGuid().ToString("N");

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
                throw new ObjectDisposedException(nameof(AliyunRealtimeAsrClient));
        }

        private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        private void RaisePartialTranscript(string text)
        {
            SafeRaise(PartialTranscriptReceived, text);
        }

        private void RaiseFinalTranscript(string text)
        {
            SafeRaise(FinalTranscriptReceived, text);
        }

        private void RaiseStatus(string text)
        {
            SafeRaise(StatusChanged, text);
        }

        private void RaiseRawMessage(string text)
        {
            SafeRaise(RawMessageReceived, text);
        }

        private void RaiseError(Exception ex)
        {
            SafeRaise(ErrorOccurred, ex);
        }

        private void RaiseSessionFinished()
        {
            SafeRaise(SessionFinished, EventArgs.Empty);
        }

        private void SafeRaise(EventHandler<string>? handler, string value)
        {
            if (handler == null)
                return;

            foreach (EventHandler<string> single in handler.GetInvocationList())
            {
                try { single(this, value); }
                catch (Exception ex) { Trace.TraceError("AliyunRealtimeAsrClient event handler failed: {0}", ex); }
            }
        }

        private void SafeRaise(EventHandler? handler, EventArgs args)
        {
            if (handler == null)
                return;

            foreach (EventHandler single in handler.GetInvocationList())
            {
                try { single(this, args); }
                catch (Exception ex) { Trace.TraceError("AliyunRealtimeAsrClient event handler failed: {0}", ex); }
            }
        }

        private void SafeRaise<T>(EventHandler<T>? handler, T args)
        {
            if (handler == null)
                return;

            foreach (EventHandler<T> single in handler.GetInvocationList())
            {
                try { single(this, args); }
                catch (Exception ex) { Trace.TraceError("AliyunRealtimeAsrClient event handler failed: {0}", ex); }
            }
        }
    }
}
