using System;
using System.Net;
using System.Net.Http;

namespace KkjQuicker.Net.Http
{
    /// <summary>
    /// 提供进程级默认 <see cref="HttpClient"/> 实例以及独立实例创建方法。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 在 .NET Framework 4.7.2 中频繁创建并销毁 <see cref="HttpClient"/>，
    /// 容易导致底层连接长期处于 TIME_WAIT 状态，从而引发 Socket 资源耗尽。
    /// </para>
    /// <para>
    /// 本类通过提供长生命周期默认实例，降低此类问题发生的概率。
    /// </para>
    /// <para>
    /// <see cref="Default"/> 与当前进程同生命周期，不应由调用方释放，
    /// 也不应放入 <c>using</c> 语句块中。
    /// </para>
    /// </remarks>
    public static class HttpClients
    {
        /// <summary>
        /// 获取进程级默认 <see cref="HttpClient"/> 实例。
        /// </summary>
        /// <remarks>
        /// 默认配置：
        /// <list type="bullet">
        /// <item><description>启用 GZip / Deflate 自动解压。</description></item>
        /// <item><description>默认超时时间为 30 秒。</description></item>
        /// </list>
        /// </remarks>
        public static HttpClient Default { get; } =
            CreateCore(null, TimeSpan.FromSeconds(30), false);

        /// <summary>
        /// 创建一个独立的 <see cref="HttpClient"/> 实例。
        /// </summary>
        /// <param name="configureHandler">
        /// 可选的处理器配置回调，用于设置代理、证书验证、自动解压等行为。
        /// 传入 <see langword="null"/> 时使用默认配置。
        /// </param>
        /// <param name="timeout">
        /// 可选的请求超时时间。
        /// 传入 <see langword="null"/> 时默认为 30 秒。
        /// 传入 <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> 可禁用超时。
        /// </param>
        /// <returns>新的独立 <see cref="HttpClient"/> 实例。</returns>
        /// <remarks>
        /// <para>
        /// 返回实例的生命周期由调用方负责管理。
        /// </para>
        /// <para>
        /// 若该实例会被频繁使用，建议将其作为字段或单例长期持有，
        /// 而非在短生命周期方法中反复创建。
        /// </para>
        /// </remarks>
        public static HttpClient Create(
            Action<HttpClientHandler>? configureHandler = null,
            TimeSpan? timeout = null)
        {
            return CreateCore(configureHandler, timeout ?? TimeSpan.FromSeconds(30), true);
        }

        /// <summary>
        /// 创建 <see cref="HttpClient"/> 的内部实现。
        /// </summary>
        /// <param name="configureHandler">处理器配置回调。</param>
        /// <param name="timeout">请求超时时间。</param>
        /// <param name="disposeHandler">
        /// 是否在 <see cref="HttpClient"/> 释放时同时释放处理器。
        /// </param>
        /// <returns>配置完成的 <see cref="HttpClient"/> 实例。</returns>
        private static HttpClient CreateCore(
            Action<HttpClientHandler> configureHandler,
            TimeSpan timeout,
            bool disposeHandler)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate
            };

            if (configureHandler != null)
            {
                configureHandler(handler);
            }

            return new HttpClient(handler, disposeHandler)
            {
                Timeout = timeout
            };
        }
    }
}