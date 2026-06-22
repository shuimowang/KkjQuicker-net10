using System;

namespace KkjQuicker.Audio.Options
{
    /// <summary>
    /// 音频采集配置。
    /// </summary>
    /// <remarks>
    /// 默认配置为语音识别常用格式：16000Hz、16bit、单声道、50ms 缓冲。
    /// </remarks>
    public sealed class AudioCaptureOptions
    {
        private int _sampleRate = 16000;
        private int _bitsPerSample = 16;
        private int _channels = 1;
        private int _bufferMilliseconds = 50;

        /// <summary>
        /// 获取或设置采样率（Hz）。
        /// </summary>
        /// <remarks>
        /// 常用值：8000（电话质量）、16000（语音识别标准）、44100（CD 质量）、48000（专业音频）。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">值必须大于 0。</exception>
        public int SampleRate
        {
            get { return _sampleRate; }
            set
            {
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "采样率必须大于 0。");
                _sampleRate = value;
            }
        }

        /// <summary>
        /// 获取或设置采样位数（bit）。
        /// </summary>
        /// <remarks>
        /// 支持 8、16、24、32 位。语音识别通常使用 16 位。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">值必须是 8、16、24 或 32。</exception>
        public int BitsPerSample
        {
            get { return _bitsPerSample; }
            set
            {
                if (value != 8 && value != 16 && value != 24 && value != 32)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "采样位数必须是 8、16、24 或 32。");
                _bitsPerSample = value;
            }
        }

        /// <summary>
        /// 获取或设置声道数。
        /// </summary>
        /// <remarks>
        /// 1 为单声道，2 为立体声。语音识别通常推荐使用单声道。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">值必须是 1 或 2。</exception>
        public int Channels
        {
            get { return _channels; }
            set
            {
                if (value != 1 && value != 2)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "声道数必须是 1 或 2。");
                _channels = value;
            }
        }

        /// <summary>
        /// 获取或设置缓冲区长度（毫秒）。
        /// </summary>
        /// <remarks>
        /// 较小的值（如 20ms）降低延迟但增加 CPU 开销；
        /// 较大的值（如 100ms）降低 CPU 开销但增加延迟。
        /// 默认 50ms 是延迟与性能的平衡点。
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">值必须位于 1 到 1000 之间。</exception>
        public int BufferMilliseconds
        {
            get { return _bufferMilliseconds; }
            set
            {
                if (value <= 0 || value > 1000)
                    throw new ArgumentOutOfRangeException(nameof(value), value, "缓冲区毫秒数必须位于 1 到 1000 之间。");
                _bufferMilliseconds = value;
            }
        }
    }
}