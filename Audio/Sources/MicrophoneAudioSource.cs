using KkjQuicker.Audio.Abstractions;
using KkjQuicker.Audio.Options;
using NAudio.Wave;
using System;
using System.Diagnostics;

namespace KkjQuicker.Audio.Sources
{
    /// <summary>
    /// 麦克风音频源。
    /// </summary>
    /// <remarks>
    /// 使用 <see cref="WaveInEvent"/> 采集麦克风输入，输出符合配置格式的 PCM 数据。
    /// </remarks>
    public sealed class MicrophoneAudioSource : IAudioFrameSource
    {
        private readonly object _lock = new object();
        private readonly AudioCaptureOptions _options;
        private readonly WaveFormat _outputFormat;

        private WaveInEvent? _waveIn = null!;
        private bool _isRunning;
        private bool _disposed;
        private bool _stopping;

        /// <summary>
        /// 当产生新的 PCM 音频帧时触发。
        /// </summary>
        public event EventHandler<byte[]>? FrameReady;

        /// <summary>
        /// 当采集过程中发生错误时触发。
        /// </summary>
        public event EventHandler<Exception>? ErrorOccurred;

        /// <summary>
        /// 获取输出音频格式。
        /// </summary>
        public WaveFormat OutputFormat
        {
            get { return _outputFormat; }
        }

        /// <summary>
        /// 获取当前是否处于采集状态。
        /// </summary>
        public bool IsRunning
        {
            get
            {
                lock (_lock)
                {
                    return _isRunning;
                }
            }
        }

        /// <summary>
        /// 创建麦克风音频源。
        /// </summary>
        /// <param name="options">音频采集配置。</param>
        public MicrophoneAudioSource(AudioCaptureOptions? options = null)
        {
            _options = options ?? new AudioCaptureOptions();
            _outputFormat = new WaveFormat(
                _options.SampleRate,
                _options.BitsPerSample,
                _options.Channels);
        }

        /// <summary>
        /// 开始采集麦克风音频。
        /// </summary>
        /// <exception cref="ObjectDisposedException">对象已被释放。</exception>
        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(MicrophoneAudioSource));

            lock (_lock)
            {
                if (_isRunning)
                    return;

                WaveInEvent? waveIn = null;
                try
                {
                    waveIn = new WaveInEvent
                    {
                        WaveFormat = _outputFormat,
                        BufferMilliseconds = _options.BufferMilliseconds
                    };

                    waveIn.DataAvailable += OnDataAvailable;
                    waveIn.RecordingStopped += OnRecordingStopped;

                    _waveIn = waveIn;
                    _stopping = false;
                    _isRunning = true;

                    waveIn.StartRecording();
                }
                catch
                {
                    if (waveIn != null)
                    {
                        waveIn.DataAvailable -= OnDataAvailable;
                        waveIn.RecordingStopped -= OnRecordingStopped;
                        waveIn.Dispose();
                    }

                    _waveIn = null;
                    _stopping = false;
                    _isRunning = false;
                    throw;
                }
            }
        }

        /// <summary>
        /// 停止采集麦克风音频。
        /// </summary>
        public void Stop()
        {
            WaveInEvent? waveIn = null;

            lock (_lock)
            {
                if (!_isRunning || _stopping)
                    return;

                _stopping = true;
                waveIn = _waveIn;
            }

            if (waveIn != null)
            {
                try
                {
                    waveIn.StopRecording();
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                    CleanupWaveIn(waveIn);
                }
            }
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Stop();

            lock (_lock)
            {
                if (_waveIn == null)
                {
                    _isRunning = false;
                    _stopping = false;
                }
            }
        }

        /// <summary>
        /// 处理音频数据可用事件。
        /// </summary>
        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            lock (_lock)
            {
                if (_disposed || !_isRunning || _stopping || e.BytesRecorded <= 0)
                    return;
            }

            var buffer = new byte[e.BytesRecorded];
            Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);

            RaiseFrameReady(buffer);
        }

        /// <summary>
        /// 处理录音停止事件。
        /// </summary>
        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            var waveIn = sender as WaveInEvent;
            CleanupWaveIn(waveIn);

            if (e != null && e.Exception != null)
                RaiseError(e.Exception);
        }

        private void CleanupWaveIn(WaveInEvent? waveIn)
        {
            lock (_lock)
            {
                if (waveIn != null)
                {
                    waveIn.DataAvailable -= OnDataAvailable;
                    waveIn.RecordingStopped -= OnRecordingStopped;

                    try
                    {
                        waveIn.Dispose();
                    }
                    catch
                    {
                    }

                    if (ReferenceEquals(_waveIn, waveIn))
                        _waveIn = null;
                }
                else if (_waveIn != null)
                {
                    _waveIn.DataAvailable -= OnDataAvailable;
                    _waveIn.RecordingStopped -= OnRecordingStopped;

                    try
                    {
                        _waveIn.Dispose();
                    }
                    catch
                    {
                    }

                    _waveIn = null;
                }

                _isRunning = false;
                _stopping = false;
            }
        }

        private void RaiseError(Exception ex)
        {
            var handler = ErrorOccurred;
            if (handler == null)
                return;

            try
            {
                handler(this, ex);
            }
            catch (Exception handlerException)
            {
                Trace.TraceError("MicrophoneAudioSource ErrorOccurred handler failed: {0}", handlerException);
            }
        }

        private void RaiseFrameReady(byte[] buffer)
        {
            var handler = FrameReady;
            if (handler == null)
                return;

            foreach (EventHandler<byte[]> single in handler.GetInvocationList())
            {
                try
                {
                    single(this, buffer);
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                }
            }
        }
    }
}
