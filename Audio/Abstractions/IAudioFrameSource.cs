using NAudio.Wave;
using System;

namespace KkjQuicker.Audio.Abstractions
{
    /// <summary>
    /// 表示连续 PCM 音频帧的数据源。
    /// </summary>
    /// <remarks>
    /// <para>该接口约定三件事：</para>
    /// <list type="number">
    /// <item><description><see cref="FrameReady"/> 事件提供合法的 PCM 数据。</description></item>
    /// <item><description><see cref="OutputFormat"/> 明确指定输出音频格式。</description></item>
    /// <item><description><see cref="IsRunning"/> 反映当前采集状态。</description></item>
    /// </list>
    /// <para>
    /// 事件回调不保证线程上下文，调用方如需更新 UI，应自行切换到 UI 线程。
    /// </para>
    /// </remarks>
    public interface IAudioFrameSource : IDisposable
    {
        /// <summary>
        /// 当产生新的 PCM 音频帧时触发。
        /// </summary>
        /// <remarks>
        /// 事件参数为符合 <see cref="OutputFormat"/> 格式的 PCM 数据。
        /// 具体数据来源与缓冲策略由实现类型决定。
        /// </remarks>
        event EventHandler<byte[]> FrameReady;

        /// <summary>
        /// 当采集过程中发生错误时触发。
        /// </summary>
        /// <remarks>
        /// 该事件用于报告底层设备异常或处理异常。
        /// 是否继续采集由具体实现决定。
        /// </remarks>
        event EventHandler<Exception> ErrorOccurred;

        /// <summary>
        /// 获取输出音频格式（采样率、位深、声道数）。
        /// </summary>
        /// <remarks>
        /// 该属性在对象创建后即可访问，不受 <see cref="Start"/> 影响。
        /// </remarks>
        WaveFormat OutputFormat { get; }

        /// <summary>
        /// 获取当前是否处于采集状态。
        /// </summary>
        /// <remarks>
        /// 返回 <see langword="true"/> 不保证底层设备一定健康，仅表示采集流程处于运行状态。
        /// </remarks>
        bool IsRunning { get; }

        /// <summary>
        /// 开始采集音频数据。
        /// </summary>
        /// <remarks>
        /// 多次调用是安全的，重复调用将被忽略。
        /// </remarks>
        /// <exception cref="ObjectDisposedException">对象已被释放。</exception>
        void Start();

        /// <summary>
        /// 停止采集音频数据。
        /// </summary>
        /// <remarks>
        /// 多次调用是安全的，重复调用将被忽略。
        /// </remarks>
        void Stop();
    }
}