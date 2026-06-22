using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KkjQuicker.Net.Http
{
    /// <summary>
    /// 提供对 Server-Sent Events（SSE）响应流的逐条读取支持。
    /// </summary>
    /// <remarks>
    /// <para>适用于 .NET Framework 4.7.2 环境，不依赖异步流特性。</para>
    /// <para>读取逻辑遵循 SSE 常见约定：</para>
    /// <list type="bullet">
    ///   <item><description>支持多行 data: 拼接。</description></item>
    ///   <item><description>以空行表示一条事件结束。</description></item>
    ///   <item><description>忽略以 : 开头的注释或心跳行。</description></item>
    ///   <item><description>自动识别并终止于 [DONE]。</description></item>
    /// </list>
    /// <para>当前仅提取 data: 字段，不解析 event:、id:、retry: 等其他 SSE 字段。</para>
    /// </remarks>
    public static class SseStreamReader
    {
        /// <summary>
        /// 异步逐条读取 SSE 响应中的 data: 消息。
        /// </summary>
        /// <param name="response">
        /// 已获取到的 HTTP 响应对象。建议通过
        /// <see cref="HttpCompletionOption.ResponseHeadersRead"/> 发送请求以尽早开始流式读取。
        /// </param>
        /// <param name="onMessage">读取到一条完整消息时执行的回调。</param>
        /// <param name="cancellationToken">
        /// 取消令牌。注意：.NET Framework 的 ReadLineAsync 不接受取消令牌，
        /// 取消仅在两次读行之间生效，长行读取期间不会立即中断。
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="response"/> 或 <paramref name="onMessage"/> 为 null 时抛出。
        /// </exception>
        /// <exception cref="OperationCanceledException">读取过程被取消。</exception>
        public static async Task ReadAsync(
            HttpResponseMessage response,
            Action<string> onMessage,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            if (response == null) throw new ArgumentNullException(nameof(response));
            if (onMessage == null) throw new ArgumentNullException(nameof(onMessage));

            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                var builder = new StringBuilder(256);

                while (true)
                {
                    // ReadLineAsync 在 .NET Framework 下不接受 CancellationToken，
                    // 故在每次读行前手动检查，取消粒度为行与行之间。
                    cancellationToken.ThrowIfCancellationRequested();

                    var line = await reader.ReadLineAsync().ConfigureAwait(false);

                    if (line == null)
                        break;

                    // 空行：一条完整事件结束
                    if (line.Length == 0)
                    {
                        if (TryFlush(builder, onMessage))
                            return;
                        continue;
                    }

                    // 注释 / 心跳行
                    if (line[0] == ':')
                        continue;

                    // 仅处理 data: 字段
                    if (!line.StartsWith("data:", StringComparison.Ordinal))
                        continue;

                    var data = line.Substring(5);
                    if (data.Length > 0 && data[0] == ' ')
                        data = data.Substring(1);

                    if (builder.Length > 0)
                        builder.Append('\n');

                    builder.Append(data);
                }

                // 流结束时若缓冲区仍有内容则补发一次
                if (builder.Length > 0)
                    TryFlush(builder, onMessage);
            }
        }

        /// <summary>
        /// 将缓冲区内容提交给回调并清空缓冲区。
        /// </summary>
        /// <returns>若内容为 [DONE] 则返回 true，表示应终止读取。</returns>
        private static bool TryFlush(StringBuilder builder, Action<string> onMessage)
        {
            if (builder.Length == 0)
                return false;

            var payload = builder.ToString();
            builder.Clear();

            if (string.Equals(payload, "[DONE]", StringComparison.Ordinal))
                return true;

            onMessage(payload);
            return false;
        }
    }
}