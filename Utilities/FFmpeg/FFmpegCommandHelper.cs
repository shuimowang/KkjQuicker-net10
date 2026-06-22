using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;

namespace KkjQuicker.Utilities.FFmpeg
{
    /// <summary>
    /// 表示 FFmpeg 屏幕区域录制参数。
    /// <para>
    /// 本类型仅描述录制行为本身，不包含倒计时、UI 或其它显示逻辑。
    /// </para>
    /// </summary>
    public sealed class FFmpegScreenRecordOptions
    {
        /// <summary>
        /// 获取或设置录制区域左上角 X 坐标（屏幕像素）。
        /// </summary>
        public int X { get; set; }

        /// <summary>
        /// 获取或设置录制区域左上角 Y 坐标（屏幕像素）。
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        /// 获取或设置录制区域宽度（像素）。
        /// <para>MP4 输出时会自动向下规范为偶数尺寸（libx264 要求）。</para>
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// 获取或设置录制区域高度（像素）。
        /// <para>MP4 输出时会自动向下规范为偶数尺寸（libx264 要求）。</para>
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// 获取或设置视频帧率（FPS）。
        /// 默认值：30
        /// </summary>
        public int Fps { get; set; } = 30;

        /// <summary>
        /// 获取或设置是否在录制画面中显示鼠标光标。
        /// 默认值：true
        /// </summary>
        public bool CaptureCursor { get; set; } = true;

        /// <summary>
        /// 获取或设置是否录制麦克风音频。
        /// <para>
        /// 启用时将自动枚举 DirectShow 设备并查找默认麦克风。
        /// 未找到合适设备时将静默跳过，不影响视频录制。
        /// 仅对 MP4 输出有效，GIF 输出时忽略。
        /// </para>
        /// </summary>
        public bool CaptureMic { get; set; }

        /// <summary>
        /// 获取或设置是否录制系统声音（如立体声混音、虚拟声卡等）。
        /// <para>
        /// 启用时将自动枚举 DirectShow 设备并查找系统音频采集设备。
        /// 未找到合适设备时将静默跳过。
        /// 仅对 MP4 输出有效，GIF 输出时忽略。
        /// </para>
        /// </summary>
        public bool CaptureSystemAudio { get; set; }

        /// <summary>
        /// 获取或设置输出格式。
        /// 支持 mp4（默认）和 gif。
        /// </summary>
        public string OutputKind { get; set; } = "mp4";

        /// <summary>
        /// 获取或设置输出文件已存在时是否覆盖。
        /// 默认值：true
        /// </summary>
        public bool OverwriteOutput { get; set; } = true;

        /// <summary>
        /// 获取或设置录制时长（秒）。
        /// 小于等于 0 表示不限时。
        /// </summary>
        public double DurationSeconds { get; set; }

        /// <summary>
        /// 获取或设置视频编码器。
        /// 默认：libx264
        /// </summary>
        public string VideoCodec { get; set; } = "libx264";

        /// <summary>
        /// 获取或设置编码预设（preset）。
        /// 默认：ultrafast（优先保证实时性）
        /// </summary>
        public string Preset { get; set; } = "ultrafast";

        /// <summary>
        /// 获取或设置 CRF 质量值。
        /// 推荐范围：18–28，默认：23
        /// </summary>
        public int Crf { get; set; } = 23;

        /// <summary>
        /// 获取或设置像素格式。
        /// 默认：yuv420p（兼容性最佳）
        /// </summary>
        public string PixelFormat { get; set; } = "yuv420p";

        /// <summary>
        /// 获取或设置 GIF 输出时缩放宽度。
        /// 小于等于 0 表示不缩放。
        /// </summary>
        public int GifScaleWidth { get; set; }

        /// <summary>
        /// 获取或设置 GIF 输出帧率。
        /// 小于等于 0 时使用 <see cref="Fps"/>。
        /// </summary>
        public int GifFps { get; set; }

        /// <summary>
        /// 获取或设置输入线程队列大小。默认：512
        /// <para>对实时输入稳定性有帮助，同时应用于视频和音频输入。</para>
        /// </summary>
        public int InputThreadQueueSize { get; set; } = 512;

        /// <summary>
        /// 获取或设置音频输出采样率（Hz）。默认：44100
        /// <para>仅在 <see cref="CaptureMic"/> 或 <see cref="CaptureSystemAudio"/> 生效时使用。</para>
        /// </summary>
        public int AudioSampleRate { get; set; } = 44100;

        /// <summary>
        /// 获取或设置音频输出声道数。默认：2（立体声）
        /// <para>仅在 <see cref="CaptureMic"/> 或 <see cref="CaptureSystemAudio"/> 生效时使用。</para>
        /// </summary>
        public int AudioChannels { get; set; } = 2;

        /// <summary>
        /// 获取或设置 DirectShow 音频输入缓冲区大小（毫秒）。默认：120
        /// <para>仅在 <see cref="CaptureMic"/> 或 <see cref="CaptureSystemAudio"/> 生效时使用。</para>
        /// </summary>
        public int AudioBufferSize { get; set; } = 120;

        /// <summary>
        /// 获取或设置自定义视频滤镜，将作为 -vf 参数传递给 FFmpeg。
        /// <para>
        /// 可用于裁剪、缩放、水印、文本叠加等。
        /// 请勿包含流标签（如 [0:v]），本类会在需要时自动处理。
        /// 同时启用双路音频时，滤镜将自动并入 -filter_complex 以避免冲突。
        /// </para>
        /// </summary>
        public string? CustomVideoFilter { get; set; }
    }

    /// <summary>
    /// 提供 FFmpeg 屏幕录制命令行构建辅助功能。
    /// <para>本类专注于命令行参数构建，不负责进程启动或 UI 控制。</para>
    /// </summary>
    public static class FFmpegCommandHelper
    {
        // 仅用于 DirectShow 设备枚举的内部类型
        private sealed class AudioDeviceEntry
        {
            public string DisplayName = null!;
            public string? AlternativeName;
        }

        /// <summary>
        /// 构建 FFmpeg 屏幕录制参数字符串。
        /// <para>
        /// 若 <see cref="FFmpegScreenRecordOptions.CaptureMic"/> 或
        /// <see cref="FFmpegScreenRecordOptions.CaptureSystemAudio"/> 为 <see langword="true"/>，
        /// 将通过 <paramref name="ffmpegExePath"/> 枚举 DirectShow 音频设备并自动写入命令行。
        /// 若对应设备未找到，该音频源将被静默跳过，不影响视频录制。
        /// </para>
        /// </summary>
        /// <param name="ffmpegExePath">ffmpeg.exe 路径。</param>
        /// <param name="outputPath">输出文件路径。</param>
        /// <param name="options">录制参数。</param>
        /// <returns>可直接传入 FFmpeg 进程的参数字符串（不含 ffmpeg.exe 自身）。</returns>
        public static string BuildScreenRecordArguments(
            string ffmpegExePath,
            string outputPath,
            FFmpegScreenRecordOptions options)
        {
            if (string.IsNullOrWhiteSpace(ffmpegExePath))
                throw new ArgumentNullException(nameof(ffmpegExePath));

            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentNullException(nameof(outputPath));

            if (options == null)
                throw new ArgumentNullException(nameof(options));

            if (options.Width <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.Width));

            if (options.Height <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.Height));

            if (options.Fps <= 0)
                throw new ArgumentOutOfRangeException(nameof(options.Fps));

            var normalized = NormalizeForOutput(options);
            bool isGif = IsGif(normalized.OutputKind);

            // GIF 不支持音频，跳过设备枚举
            string? micDevice = null;
            string? sysAudioDevice = null;

            if (!isGif && (normalized.CaptureMic || normalized.CaptureSystemAudio))
            {
                var devices = TryEnumerateAudioDevices(ffmpegExePath);

                if (normalized.CaptureMic)
                    micDevice = FindMicDevice(devices);

                if (normalized.CaptureSystemAudio)
                    sysAudioDevice = FindSystemAudioDevice(devices);
            }

            bool hasMic = micDevice != null;
            bool hasSysAudio = sysAudioDevice != null;
            bool hasTwoAudio = hasMic && hasSysAudio;
            bool hasAnyAudio = hasMic || hasSysAudio;

            var parts = new List<string>();

            // 1. 覆盖标志
            parts.Add(normalized.OverwriteOutput ? "-y" : "-n");

            // 2. 视频输入（gdigrab，输入索引 0）
            AppendGdiGrabInput(parts, normalized);

            // 3. 音频输入（dshow；单路为索引 1，双路为索引 1 和 2）
            if (hasMic)
                AppendDshowAudioInput(parts, micDevice, normalized);

            if (hasSysAudio)
                AppendDshowAudioInput(parts, sysAudioDevice, normalized);

            // 4. 双路音频混流（filter_complex + map）
            //    单路音频无需显式 map，FFmpeg 自动从输入 0 取视频、输入 1 取音频
            if (hasTwoAudio)
                AppendTwoAudioMixFilterAndMap(parts, normalized.CustomVideoFilter);

            // 5. 输出参数
            AppendOutputArguments(parts, normalized, hasAnyAudio, hasTwoAudio);

            // 6. 输出路径
            parts.Add(Quote(outputPath));

            return Join(parts);
        }

        // ----------------------------------------------------------------
        // 输入段构建
        // ----------------------------------------------------------------

        private static void AppendGdiGrabInput(
            List<string> parts,
            FFmpegScreenRecordOptions options)
        {
            if (options.InputThreadQueueSize > 0)
            {
                parts.Add("-thread_queue_size");
                parts.Add(ToInvariant(options.InputThreadQueueSize));
            }

            parts.Add("-f");
            parts.Add("gdigrab");

            parts.Add("-framerate");
            parts.Add(ToInvariant(options.Fps));

            parts.Add("-offset_x");
            parts.Add(ToInvariant(options.X));

            parts.Add("-offset_y");
            parts.Add(ToInvariant(options.Y));

            parts.Add("-video_size");
            parts.Add($"{options.Width}x{options.Height}");

            parts.Add("-draw_mouse");
            parts.Add(options.CaptureCursor ? "1" : "0");

            if (options.DurationSeconds > 0)
            {
                parts.Add("-t");
                parts.Add(options.DurationSeconds.ToString("0.###", CultureInfo.InvariantCulture));
            }

            parts.Add("-i");
            parts.Add("desktop");
        }

        private static void AppendDshowAudioInput(
            List<string> parts,
            string? deviceName,
            FFmpegScreenRecordOptions options)
        {
            if (options.InputThreadQueueSize > 0)
            {
                parts.Add("-thread_queue_size");
                parts.Add(ToInvariant(options.InputThreadQueueSize));
            }

            parts.Add("-f");
            parts.Add("dshow");

            if (options.AudioBufferSize > 0)
            {
                parts.Add("-audio_buffer_size");
                parts.Add(ToInvariant(options.AudioBufferSize));
            }

            parts.Add("-i");
            // deviceName 可能含空格（显示名）或特殊字符（替代名），交由 Quote 统一处理
            parts.Add(Quote("audio=" + deviceName));
        }

        // ----------------------------------------------------------------
        // 双路音频混流
        // ----------------------------------------------------------------

        private static void AppendTwoAudioMixFilterAndMap(
            List<string> parts,
            string? customVideoFilter)
        {
            // 输入 0：gdigrab（视频）  输入 1：mic  输入 2：sysaudio
            //
            // 存在 CustomVideoFilter 时须将其并入 filter_complex，
            // 因为 -vf 与 -filter_complex 不能同时使用。
            string filterComplex;
            string videoMapValue;

            if (!string.IsNullOrWhiteSpace(customVideoFilter))
            {
                filterComplex = $"[0:v]{customVideoFilter}[vout];[1:a][2:a]amix=inputs=2[aout]";
                videoMapValue = "[vout]";
            }
            else
            {
                filterComplex = "[1:a][2:a]amix=inputs=2[aout]";
                videoMapValue = "0:v";
            }

            parts.Add("-filter_complex");
            parts.Add(Quote(filterComplex));

            parts.Add("-map");
            parts.Add(Quote(videoMapValue));  // "0:v" 无需引号，"[vout]" 含 [] 会被 Quote 加引号

            parts.Add("-map");
            parts.Add(Quote("[aout]"));
        }

        // ----------------------------------------------------------------
        // 输出段构建
        // ----------------------------------------------------------------

        private static void AppendOutputArguments(
            List<string> parts,
            FFmpegScreenRecordOptions options,
            bool hasAnyAudio,
            bool hasTwoAudio)
        {
            if (IsGif(options.OutputKind))
            {
                AppendGifArguments(parts, options);
                return;
            }

            AppendMp4Arguments(parts, options, hasAnyAudio, hasTwoAudio);
        }

        private static void AppendMp4Arguments(
            List<string> parts,
            FFmpegScreenRecordOptions options,
            bool hasAnyAudio,
            bool hasTwoAudio)
        {
            // 双路音频时 CustomVideoFilter 已并入 filter_complex，此处跳过 -vf 避免冲突
            if (!hasTwoAudio && !string.IsNullOrWhiteSpace(options.CustomVideoFilter))
            {
                parts.Add("-vf");
                parts.Add(Quote(options.CustomVideoFilter));
            }

            parts.Add("-c:v");
            parts.Add(options.VideoCodec);

            parts.Add("-preset");
            parts.Add(options.Preset);

            parts.Add("-crf");
            parts.Add(ToInvariant(options.Crf));

            parts.Add("-pix_fmt");
            parts.Add(options.PixelFormat);

            if (hasAnyAudio)
            {
                parts.Add("-ar");
                parts.Add(ToInvariant(options.AudioSampleRate));

                parts.Add("-ac");
                parts.Add(ToInvariant(options.AudioChannels));
            }
        }

        private static void AppendGifArguments(
            List<string> parts,
            FFmpegScreenRecordOptions options)
        {
            parts.Add("-vf");
            parts.Add(Quote(BuildGifFilter(options)));
        }

        private static string BuildGifFilter(FFmpegScreenRecordOptions options)
        {
            int fps = options.GifFps > 0 ? options.GifFps : options.Fps;

            if (options.GifScaleWidth > 0)
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "fps={0},scale={1}:-1:flags=lanczos",
                    fps,
                    options.GifScaleWidth);
            }

            return $"fps={fps}";
        }

        // ----------------------------------------------------------------
        // DirectShow 设备枚举（私有，合并自 FFmpegDshowDeviceHelper）
        // ----------------------------------------------------------------

        private static List<AudioDeviceEntry> TryEnumerateAudioDevices(string ffmpegExePath)
        {
            try
            {
                string output = RunListDevices(ffmpegExePath);
                return ParseAudioDeviceEntries(output);
            }
            catch
            {
                return new List<AudioDeviceEntry>();
            }
        }

        private static string RunListDevices(string ffmpegExePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExePath,
                Arguments = "-hide_banner -list_devices true -f dshow -i dummy",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // FFmpeg 在 Windows 上以系统 ANSI 编码输出设备名称，
                // 必须使用 Encoding.Default 才能正确解析含中文的设备名。
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default,
            };

            string? dir = TryGetDirectoryName(ffmpegExePath);
            if (!string.IsNullOrWhiteSpace(dir))
                psi.WorkingDirectory = dir;

            using (var process = new Process { StartInfo = psi })
            {
                process.Start();
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                // FFmpeg 将设备列表写入 stderr，stdout 通常为空
                return stderr + Environment.NewLine + stdout;
            }
        }

        private static List<AudioDeviceEntry> ParseAudioDeviceEntries(string ffmpegOutput)
        {
            var result = new List<AudioDeviceEntry>();
            if (string.IsNullOrWhiteSpace(ffmpegOutput))
                return result;

            string[] lines = ffmpegOutput.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            AudioDeviceEntry? current = null;

            foreach (string line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                string trimmed = line.Trim();

                // 形如：  "麦克风 (Realtek(R) Audio)" (audio)
                if (trimmed.IndexOf("(audio)", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string? displayName = TryParseQuotedText(trimmed);
                    if (displayName == null)
                        continue;

                    current = new AudioDeviceEntry { DisplayName = displayName };
                    result.Add(current);
                    continue;
                }

                // 形如：    Alternative name "@device_cm_{...}\wave_{...}"
                if (current != null &&
                    trimmed.IndexOf("Alternative name", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    current.AlternativeName = TryParseQuotedText(trimmed);
                    current = null;
                }
            }

            return result;
        }

        /// <summary>
        /// 查找默认麦克风设备。
        /// 优先按关键词匹配；未匹配时回退到列表中第一个可用设备。
        /// </summary>
        private static string? FindMicDevice(List<AudioDeviceEntry> devices)
        {
            if (devices == null || devices.Count == 0)
                return null;

            foreach (var device in devices)
            {
                string display = device.DisplayName ?? string.Empty;
                string lower = display.ToLowerInvariant();

                if (lower.Contains("microphone") || lower.Contains("mic") || display.Contains("麦"))
                    return GetUsableDeviceName(device);
            }

            // 无关键词匹配时，取第一个可用设备作为默认麦克风
            foreach (var device in devices)
            {
                string? name = GetUsableDeviceName(device);
                if (name != null)
                    return name;
            }

            return null;
        }

        /// <summary>
        /// 查找系统声音采集设备（立体声混音、虚拟声卡等）。
        /// 未找到时返回 <see langword="null"/>，不做品牌猜测以避免误选麦克风。
        /// </summary>
        private static string? FindSystemAudioDevice(List<AudioDeviceEntry> devices)
        {
            if (devices == null || devices.Count == 0)
                return null;

            foreach (var device in devices)
            {
                string display = device.DisplayName ?? string.Empty;
                string lower = display.ToLowerInvariant();

                if (lower.Contains("stereo mix") ||
                    display.Contains("立体声混音") ||
                    lower.Contains("wave out") ||
                    lower.Contains("loopback") ||
                    lower.Contains("virtual-audio-capturer"))
                {
                    return GetUsableDeviceName(device);
                }
            }

            return null;
        }

        private static string? GetUsableDeviceName(AudioDeviceEntry device)
        {
            if (device == null)
                return null;

            // 优先使用替代名（FFmpeg 内部路径形式，稳定性更高）
            if (!string.IsNullOrWhiteSpace(device.AlternativeName))
                return device.AlternativeName;

            if (!string.IsNullOrWhiteSpace(device.DisplayName))
                return device.DisplayName;

            return null;
        }

        private static string? TryParseQuotedText(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            int first = line.IndexOf('"');
            if (first < 0)
                return null;

            int second = line.IndexOf('"', first + 1);
            if (second <= first)
                return null;

            string text = line.Substring(first + 1, second - first - 1).Trim();
            return text.Length == 0 ? null : text;
        }

        // ----------------------------------------------------------------
        // 规范化
        // ----------------------------------------------------------------

        private static FFmpegScreenRecordOptions NormalizeForOutput(FFmpegScreenRecordOptions options)
        {
            if (!IsMp4(options.OutputKind))
                return options;

            // 维护提示：向 FFmpegScreenRecordOptions 新增属性时必须同步更新此处。
            return new FFmpegScreenRecordOptions
            {
                X = options.X,
                Y = options.Y,
                Width = NormalizeEvenDown(options.Width),
                Height = NormalizeEvenDown(options.Height),
                Fps = options.Fps,
                CaptureCursor = options.CaptureCursor,
                CaptureMic = options.CaptureMic,
                CaptureSystemAudio = options.CaptureSystemAudio,
                OutputKind = options.OutputKind,
                OverwriteOutput = options.OverwriteOutput,
                DurationSeconds = options.DurationSeconds,
                VideoCodec = options.VideoCodec,
                Preset = options.Preset,
                Crf = options.Crf,
                PixelFormat = options.PixelFormat,
                GifScaleWidth = options.GifScaleWidth,
                GifFps = options.GifFps,
                InputThreadQueueSize = options.InputThreadQueueSize,
                AudioSampleRate = options.AudioSampleRate,
                AudioChannels = options.AudioChannels,
                AudioBufferSize = options.AudioBufferSize,
                CustomVideoFilter = options.CustomVideoFilter,
            };
        }

        private static int NormalizeEvenDown(int value) => (value & 1) == 0 ? value : value - 1;

        private static bool IsMp4(string kind) =>
            string.Equals(kind, "mp4", StringComparison.OrdinalIgnoreCase);

        private static bool IsGif(string kind) =>
            string.Equals(kind, "gif", StringComparison.OrdinalIgnoreCase);

        // ----------------------------------------------------------------
        // 引号与字符串辅助
        // ----------------------------------------------------------------

        /// <summary>
        /// 对字符串进行命令行安全引号包裹。
        /// </summary>
        public static string Quote(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "\"\"";

            bool hasDoubleQuote = value.Contains("\"");
            if (hasDoubleQuote)
                value = value.Replace("\"", "\\\"");

            // 含转义引号时也必须加外层引号，否则裸 \" 不合法
            return (hasDoubleQuote || NeedsQuote(value))
                ? "\"" + value + "\""
                : value;
        }

        private static bool NeedsQuote(string value)
        {
            foreach (char ch in value)
            {
                if (char.IsWhiteSpace(ch) ||
                    ch == '&' || ch == '(' || ch == ')' ||
                    ch == '[' || ch == ']' || ch == ';')
                    return true;
            }

            return false;
        }

        private static string Join(IList<string> parts)
        {
            var sb = new StringBuilder();

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part))
                    continue;

                if (sb.Length > 0)
                    sb.Append(' ');

                sb.Append(part);
            }

            return sb.ToString();
        }

        private static string ToInvariant(int value) =>
            value.ToString(CultureInfo.InvariantCulture);

        private static string? TryGetDirectoryName(string path)
        {
            try { return Path.GetDirectoryName(path); }
            catch { return null; }
        }
    }
}