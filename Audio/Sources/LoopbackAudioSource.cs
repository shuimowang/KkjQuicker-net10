using KkjQuicker.Audio.Abstractions;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Diagnostics;

namespace KkjQuicker.Audio.Sources
{
    /// <summary>
    /// 系统声音回环音频源（What U Hear / 立体声混音）。
    /// </summary>
    /// <remarks>
    /// 采集系统正在播放的声音，并输出 16000Hz、16-bit、Mono 的 PCM 数据，
    /// 适用于语音识别场景。
    /// <para>
    /// 支持系统输出设备为单声道、立体声（2 声道）及 5.1/7.1 等多声道格式；
    /// 多声道情况下取第一声道（Left Front）混入单声道输出。
    /// </para>
    /// </remarks>
    public sealed class LoopbackAudioSource : IAudioFrameSource
    {
        private readonly object _lock = new object();
        private readonly WaveFormat _outputFormat;
        private readonly int _targetSampleRate = 16000;

        private WasapiLoopbackCapture _capture;
        private BufferedWaveProvider _bufferedProvider;
        private WdlResamplingSampleProvider _resampler;
        private bool _isRunning;
        private bool _disposed;
        private bool _stopping;

        /// <summary>
        /// 当产生新的 PCM 音频帧时触发。
        /// </summary>
        /// <remarks>
        /// 事件参数为 16000Hz、16-bit、Mono 的 PCM 数据。
        /// </remarks>
        public event EventHandler<byte[]> FrameReady;

        /// <summary>
        /// 当采集过程中发生错误时触发。
        /// </summary>
        public event EventHandler<Exception> ErrorOccurred;

        /// <summary>
        /// 获取输出音频格式：16000Hz、16-bit、Mono。
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
        /// 创建系统回环音频源。
        /// </summary>
        public LoopbackAudioSource()
        {
            _outputFormat = new WaveFormat(_targetSampleRate, 16, 1);
        }

        /// <summary>
        /// 开始采集系统音频。
        /// </summary>
        /// <exception cref="ObjectDisposedException">对象已被释放。</exception>
        public void Start()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(LoopbackAudioSource));

            lock (_lock)
            {
                if (_isRunning)
                    return;

                WasapiLoopbackCapture capture = null;
                BufferedWaveProvider bufferedProvider = null;
                WdlResamplingSampleProvider resampler = null;

                try
                {
                    capture = new WasapiLoopbackCapture();
                    capture.DataAvailable += OnDataAvailable;
                    capture.RecordingStopped += OnRecordingStopped;

                    bufferedProvider = new BufferedWaveProvider(capture.WaveFormat)
                    {
                        DiscardOnBufferOverflow = true,
                        BufferLength = capture.WaveFormat.AverageBytesPerSecond * 2
                    };

                    // Fix #1：区分声道数，避免 StereoToMonoSampleProvider 在 > 2 声道时崩溃。
                    // WasapiLoopbackCapture 默认跟随系统输出设备格式，5.1/7.1 声卡是常见场景。
                    ISampleProvider sampleProvider = bufferedProvider.ToSampleProvider();

                    if (capture.WaveFormat.Channels == 2)
                    {
                        sampleProvider = new StereoToMonoSampleProvider(sampleProvider)
                        {
                            LeftVolume = 0.5f,
                            RightVolume = 0.5f
                        };
                    }
                    else if (capture.WaveFormat.Channels > 2)
                    {
                        // 多声道（5.1 / 7.1 等）：仅取第一声道（Left Front）输出单声道。
                        var mux = new MultiplexingSampleProvider(new ISampleProvider[] { sampleProvider }, 1);
                        mux.ConnectInputToOutput(0, 0);
                        sampleProvider = mux;
                    }

                    resampler = new WdlResamplingSampleProvider(sampleProvider, _targetSampleRate);

                    _capture = capture;
                    _bufferedProvider = bufferedProvider;
                    _resampler = resampler;
                    _stopping = false;
                    _isRunning = true;

                    capture.StartRecording();
                }
                catch
                {
                    if (capture != null)
                    {
                        capture.DataAvailable -= OnDataAvailable;
                        capture.RecordingStopped -= OnRecordingStopped;
                        capture.Dispose();
                    }

                    _capture = null;
                    _bufferedProvider = null;
                    _resampler = null;
                    _stopping = false;
                    _isRunning = false;
                    throw;
                }
            }
        }

        /// <summary>
        /// 停止采集系统音频。
        /// </summary>
        public void Stop()
        {
            WasapiLoopbackCapture capture = null;

            lock (_lock)
            {
                if (!_isRunning || _stopping)
                    return;

                _stopping = true;
                capture = _capture;
            }

            if (capture != null)
            {
                try
                {
                    capture.StopRecording();
                }
                catch (Exception ex)
                {
                    RaiseError(ex);
                    CleanupCapture(capture);
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
                _bufferedProvider = null;
                _resampler = null;
                _isRunning = false;
                _stopping = false;
            }
        }

        /// <summary>
        /// 处理音频数据可用事件。
        /// </summary>
        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            BufferedWaveProvider bufferedProvider;
            WdlResamplingSampleProvider resampler;
            WasapiLoopbackCapture capture;

            lock (_lock)
            {
                if (_disposed || !_isRunning || _stopping || e.BytesRecorded <= 0)
                    return;

                bufferedProvider = _bufferedProvider;
                resampler = _resampler;
                capture = _capture;
            }

            if (bufferedProvider == null || resampler == null || capture == null)
                return;

            try
            {
                bufferedProvider.AddSamples(e.Buffer, 0, e.BytesRecorded);

                // Fix #2：以本次新增量（e.BytesRecorded）而非 buffer 总量估算输出采样数，
                // 避免 buffer 积压时单次回调读取量大幅波动，影响实时性。
                // +16 为 WDL 重采样器内部的少量延迟裕量，确保本次新增数据能被完整读出。
                var bytesPerSample = capture.WaveFormat.BitsPerSample / 8;
                var newInputSamples = e.BytesRecorded / bytesPerSample / capture.WaveFormat.Channels;
                var resampleRatio = (double)_targetSampleRate / capture.WaveFormat.SampleRate;
                var expectedOutputSamples = (int)(newInputSamples * resampleRatio) + 16;

                if (expectedOutputSamples <= 0)
                    return;

                var sampleBuffer = new float[expectedOutputSamples];
                var samplesRead = resampler.Read(sampleBuffer, 0, sampleBuffer.Length);

                if (samplesRead <= 0)
                    return;

                var pcmBuffer = new byte[samplesRead * 2];
                for (var i = 0; i < samplesRead; i++)
                {
                    var clamped = Math.Max(-1.0f, Math.Min(1.0f, sampleBuffer[i]));
                    var pcmSample = (short)(clamped * 32767f);

                    pcmBuffer[i * 2] = (byte)(pcmSample & 0xFF);
                    pcmBuffer[i * 2 + 1] = (byte)(pcmSample >> 8);
                }

                RaiseFrameReady(pcmBuffer);
            }
            catch (Exception ex)
            {
                // 故意不向外抛出，避免中断底层采集线程。
                RaiseError(ex);
            }
        }

        /// <summary>
        /// 处理录音停止事件。
        /// </summary>
        private void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            var capture = sender as WasapiLoopbackCapture;
            CleanupCapture(capture);

            if (e != null && e.Exception != null)
                RaiseError(e.Exception);
        }

        private void CleanupCapture(WasapiLoopbackCapture capture)
        {
            lock (_lock)
            {
                if (capture != null)
                {
                    capture.DataAvailable -= OnDataAvailable;
                    capture.RecordingStopped -= OnRecordingStopped;

                    try
                    {
                        capture.Dispose();
                    }
                    catch
                    {
                    }

                    if (ReferenceEquals(_capture, capture))
                        _capture = null;
                }
                else if (_capture != null)
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.RecordingStopped -= OnRecordingStopped;

                    try
                    {
                        _capture.Dispose();
                    }
                    catch
                    {
                    }

                    _capture = null;
                }

                _bufferedProvider = null;
                _resampler = null;
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
                Trace.TraceError("LoopbackAudioSource ErrorOccurred handler failed: {0}", handlerException);
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
