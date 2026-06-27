using System;
using System.Net;
using System.Net.Http;

namespace KkjQuicker.Net.Http
{
    /// <summary>
    /// 提供带有项目默认网络选项的 <see cref="HttpClient"/> 创建方法。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 类库不持有进程级默认实例；调用方应按自身生命周期缓存或释放返回的
    /// <see cref="HttpClient"/>。
    /// </para>
    /// <para>
    /// 默认配置启用 GZip / Deflate / Brotli 自动解压，并设置 30 秒超时。
    /// </para>
    /// </remarks>
    public static class HttpClients
    {
        /// <summary>
        /// 创建一个配置完成的 <see cref="HttpClient"/> 实例。
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
        /// <returns>新的 <see cref="HttpClient"/> 实例。</returns>
        /// <remarks>
        /// <para>
        /// 返回实例的生命周期由调用方负责管理。若该实例会被频繁使用，
        /// 建议将其作为字段或通过依赖注入长期持有。
        /// </para>
        /// </remarks>
        public static HttpClient Create(
            Action<HttpClientHandler>? configureHandler = null,
            TimeSpan? timeout = null)
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression =
                    DecompressionMethods.GZip |
                    DecompressionMethods.Deflate |
                    DecompressionMethods.Brotli
            };

            if (configureHandler != null)
                configureHandler(handler);

            return new HttpClient(handler, disposeHandler: true)
            {
                Timeout = timeout ?? TimeSpan.FromSeconds(30)
            };
        }
    }
}
