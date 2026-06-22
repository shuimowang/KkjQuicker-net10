using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KkjQuicker.Utilities.FFmpeg
{
    /// <summary>
    /// 表示 FFmpeg 进程执行结果。
    /// </summary>
    public sealed class FFmpegProcessResult
    {
        /// <summary>
        /// 获取或设置进程退出码。
        /// </summary>
        public int ExitCode { get; set; }

        /// <summary>
        /// 获取或设置标准输出文本。
        /// </summary>
        public string StandardOutput { get; set; } = null!;

        /// <summary>
        /// 获取或设置标准错误文本。
        /// </summary>
        public string StandardError { get; set; } = null!;

        /// <summary>
        /// 获取或设置是否因超时被终止。
        /// </summary>
        public bool TimedOut { get; set; }

        /// <summary>
        /// 获取或设置是否因取消请求被终止。
        /// </summary>
        public bool Canceled { get; set; }

        /// <summary>
        /// 获取或设置启动时使用的可执行文件路径。
        /// </summary>
        public string FileName { get; set; } = null!;

        /// <summary>
        /// 获取或设置启动时使用的参数。
        /// </summary>
        public string Arguments { get; set; } = null!;

        /// <summary>
        /// 获取执行结果是否成功。
        /// <para>仅当未超时、未取消且退出码为 0 时返回 <see langword="true"/>。</para>
        /// </summary>
        public bool Success
        {
            get
            {
                return !TimedOut && !Canceled && ExitCode == 0;
            }
        }
    }

    /// <summary>
    /// 提供 FFmpeg 进程执行与停止辅助功能。
    /// </summary>
    public static class FFmpegProcessHelper
    {
        /// <summary>
        /// 同步执行 FFmpeg 进程。
        /// </summary>
        /// <param name="ffmpegExePath">ffmpeg.exe 路径，由调用方保证正确。</param>
        /// <param name="arguments">命令行参数。</param>
        /// <param name="workingDirectory">工作目录。为空时自动取可执行文件所在目录。</param>
        /// <param name="timeoutMilliseconds">超时毫秒数。小于等于 0 表示不限时。</param>
        /// <param name="outputDataReceived">标准输出逐行回调。</param>
        /// <param name="errorDataReceived">标准错误逐行回调。</param>
        /// <returns>执行结果。</returns>
        /// <remarks>
        /// <para>
        /// 本方法内部通过 <c>.GetAwaiter().GetResult()</c> 阻塞等待 <see cref="RunAsync"/> 完成。
        /// 在具有 <see cref="System.Threading.SynchronizationContext"/> 的线程（如 WPF UI 线程）上调用时，
        /// 可能导致死锁。建议在 UI 线程中优先使用 <see cref="RunAsync"/>。
        /// </para>
        /// </remarks>
        public static FFmpegProcessResult Run(
            string ffmpegExePath,
            string arguments,
            string? workingDirectory = null,
            int timeoutMilliseconds = 0,
            Action<string>? outputDataReceived = null,
            Action<string>? errorDataReceived = null)
        {
            return RunAsync(
                ffmpegExePath,
                arguments,
                workingDirectory,
                timeoutMilliseconds,
                CancellationToken.None,
                outputDataReceived,
                errorDataReceived).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 异步执行 FFmpeg 进程。
        /// </summary>
        /// <param name="ffmpegExePath">ffmpeg.exe 路径，由调用方保证正确。</param>
        /// <param name="arguments">命令行参数。</param>
        /// <param name="workingDirectory">工作目录。为空时自动取可执行文件所在目录。</param>
        /// <param name="timeoutMilliseconds">超时毫秒数。小于等于 0 表示不限时。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="outputDataReceived">标准输出逐行回调。</param>
        /// <param name="errorDataReceived">标准错误逐行回调。</param>
        /// <returns>执行结果。</returns>
        public static async Task<FFmpegProcessResult> RunAsync(
            string ffmpegExePath,
            string arguments,
            string? workingDirectory = null,
            int timeoutMilliseconds = 0,
            CancellationToken cancellationToken = default(CancellationToken),
            Action<string>? outputDataReceived = null,
            Action<string>? errorDataReceived = null)
        {
            if (string.IsNullOrWhiteSpace(ffmpegExePath))
                throw new ArgumentNullException(nameof(ffmpegExePath));

            if (arguments == null)
                arguments = string.Empty;

            if (string.IsNullOrWhiteSpace(workingDirectory))
                workingDirectory = TryGetDirectoryName(ffmpegExePath);

            var result = new FFmpegProcessResult
            {
                FileName = ffmpegExePath,
                Arguments = arguments,
                ExitCode = -1,
                StandardOutput = string.Empty,
                StandardError = string.Empty
            };

            var stdoutBuilder = new StringBuilder();
            var stderrBuilder = new StringBuilder();

            using (var process = new Process())
            {
                process.StartInfo = CreateStartInfo(ffmpegExePath, arguments, workingDirectory);

                var stdoutClosed = new TaskCompletionSource<bool>();
                var stderrClosed = new TaskCompletionSource<bool>();
                var processExited = new TaskCompletionSource<bool>();

                process.OutputDataReceived += delegate (object? sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null)
                    {
                        stdoutClosed.TrySetResult(true);
                        return;
                    }

                    lock (stdoutBuilder)
                    {
                        stdoutBuilder.AppendLine(e.Data);
                    }

                    if (outputDataReceived != null)
                        outputDataReceived(e.Data);
                };

                process.ErrorDataReceived += delegate (object? sender, DataReceivedEventArgs e)
                {
                    if (e.Data == null)
                    {
                        stderrClosed.TrySetResult(true);
                        return;
                    }

                    lock (stderrBuilder)
                    {
                        stderrBuilder.AppendLine(e.Data);
                    }

                    if (errorDataReceived != null)
                        errorDataReceived(e.Data);
                };

                process.EnableRaisingEvents = true;
                process.Exited += delegate
                {
                    processExited.TrySetResult(true);
                };

                if (!process.Start())
                    throw new InvalidOperationException("无法启动 FFmpeg 进程。");

                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                Task timeoutTask = timeoutMilliseconds > 0
                    ? Task.Delay(timeoutMilliseconds)
                    : CreateNeverCompletingTask();

                Task cancelTask = cancellationToken.CanBeCanceled
                    ? Task.Delay(Timeout.Infinite, cancellationToken)
                    : CreateNeverCompletingTask();

                Task completed = await Task.WhenAny(processExited.Task, timeoutTask, cancelTask).ConfigureAwait(false);

                if (completed == timeoutTask)
                {
                    result.TimedOut = true;
                    TryStopGracefully(process, 3000);
                }
                else if (completed == cancelTask)
                {
                    result.Canceled = true;
                    TryStopGracefully(process, 3000);
                }

                if (completed != processExited.Task)
                    await WaitForExitOrTimeoutAsync(processExited.Task, 3000).ConfigureAwait(false);

                if (processExited.Task.IsCompleted)
                {
                    await WaitForExitOrTimeoutAsync(
                        Task.WhenAll(stdoutClosed.Task, stderrClosed.Task),
                        3000).ConfigureAwait(false);
                }

                result.ExitCode = SafeGetExitCode(process);
            }

            result.StandardOutput = stdoutBuilder.ToString();
            result.StandardError = stderrBuilder.ToString();

            return result;
        }

        /// <summary>
        /// 启动一个长期运行的 FFmpeg 进程并返回 <see cref="Process"/> 实例。
        /// <para>调用方负责在适当时机停止并释放该进程。</para>
        /// </summary>
        /// <param name="ffmpegExePath">ffmpeg.exe 路径，由调用方保证正确。</param>
        /// <param name="arguments">命令行参数。</param>
        /// <param name="workingDirectory">工作目录。为空时自动取可执行文件所在目录。</param>
        /// <param name="outputDataReceived">标准输出逐行回调。</param>
        /// <param name="errorDataReceived">标准错误逐行回调。</param>
        /// <returns>已启动的进程实例。</returns>
        public static Process Start(
            string ffmpegExePath,
            string arguments,
            string? workingDirectory = null,
            Action<string>? outputDataReceived = null,
            Action<string>? errorDataReceived = null)
        {
            if (string.IsNullOrWhiteSpace(ffmpegExePath))
                throw new ArgumentNullException(nameof(ffmpegExePath));

            if (arguments == null)
                arguments = string.Empty;

            if (string.IsNullOrWhiteSpace(workingDirectory))
                workingDirectory = TryGetDirectoryName(ffmpegExePath);

            var process = new Process();
            process.StartInfo = CreateStartInfo(ffmpegExePath, arguments, workingDirectory);

            process.OutputDataReceived += delegate (object? sender, DataReceivedEventArgs e)
            {
                if (e.Data != null && outputDataReceived != null)
                    outputDataReceived(e.Data);
            };

            process.ErrorDataReceived += delegate (object? sender, DataReceivedEventArgs e)
            {
                if (e.Data != null && errorDataReceived != null)
                    errorDataReceived(e.Data);
            };

            if (!process.Start())
            {
                process.Dispose();
                throw new InvalidOperationException("无法启动 FFmpeg 进程。");
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            return process;
        }

        /// <summary>
        /// 尝试优雅停止 FFmpeg 进程。
        /// <para>会先向标准输入写入 <c>q</c>，等待 FFmpeg 正常收尾退出；若超时仍未退出，再兜底强制结束。</para>
        /// </summary>
        /// <param name="process">目标进程。</param>
        /// <param name="waitMilliseconds">优雅停止等待毫秒数。默认 5000。</param>
        /// <returns>若最终进程已退出则返回 <see langword="true"/>；否则返回 <see langword="false"/>。</returns>
        public static bool TryStopGracefully(Process process, int waitMilliseconds = 5000)
        {
            if (process == null)
                return true;

            try
            {
                if (process.HasExited)
                    return true;
            }
            catch
            {
                return true;
            }

            try
            {
                if (process.StartInfo != null && process.StartInfo.RedirectStandardInput)
                {
                    process.StandardInput.WriteLine("q");
                    process.StandardInput.Flush();
                }
            }
            catch
            {
            }

            try
            {
                if (process.WaitForExit(waitMilliseconds))
                    return true;
            }
            catch
            {
            }

            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch
            {
            }

            try
            {
                return process.WaitForExit(waitMilliseconds);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 尝试强制终止指定进程。
        /// <para>仅建议在无法优雅停止时作为兜底使用。</para>
        /// </summary>
        /// <param name="process">目标进程。</param>
        public static void TryKill(Process process)
        {
            if (process == null)
                return;

            try
            {
                if (!process.HasExited)
                    process.Kill();
            }
            catch
            {
            }
        }

        /// <summary>
        /// 创建 FFmpeg 进程启动信息。
        /// </summary>
        /// <param name="ffmpegExePath">ffmpeg.exe 路径。</param>
        /// <param name="arguments">命令行参数。</param>
        /// <param name="workingDirectory">工作目录。</param>
        /// <returns>启动信息。</returns>
        public static ProcessStartInfo CreateStartInfo(string ffmpegExePath, string arguments, string? workingDirectory = null)
        {
            if (string.IsNullOrWhiteSpace(ffmpegExePath))
                throw new ArgumentNullException(nameof(ffmpegExePath));

            if (arguments == null)
                arguments = string.Empty;

            if (string.IsNullOrWhiteSpace(workingDirectory))
                workingDirectory = TryGetDirectoryName(ffmpegExePath);

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            if (!string.IsNullOrWhiteSpace(workingDirectory))
                psi.WorkingDirectory = workingDirectory;

            return psi;
        }

        private static int SafeGetExitCode(Process process)
        {
            try
            {
                return process != null ? process.ExitCode : -1;
            }
            catch
            {
                return -1;
            }
        }

        private static string? TryGetDirectoryName(string path)
        {
            try
            {
                return Path.GetDirectoryName(path);
            }
            catch
            {
                return null;
            }
        }

        private static Task CreateNeverCompletingTask()
        {
            return new TaskCompletionSource<bool>().Task;
        }

        private static async Task<bool> WaitForExitOrTimeoutAsync(Task task, int timeoutMilliseconds)
        {
            if (task == null)
                return true;

            if (task.IsCompleted)
            {
                await task.ConfigureAwait(false);
                return true;
            }

            Task timeoutTask = timeoutMilliseconds > 0
                ? Task.Delay(timeoutMilliseconds)
                : CreateNeverCompletingTask();

            Task completed = await Task.WhenAny(task, timeoutTask).ConfigureAwait(false);
            if (completed != task)
                return false;

            await task.ConfigureAwait(false);
            return true;
        }
    }
}
