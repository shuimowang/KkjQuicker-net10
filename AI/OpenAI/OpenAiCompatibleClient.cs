using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using KkjQuicker.AI.OpenAI.Models;
using KkjQuicker.Net.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace KkjQuicker.AI.OpenAI
{
    /// <summary>
    /// 兼容 OpenAI Chat Completions 协议的客户端。
    /// </summary>
    /// <remarks>
    /// <para>支持普通请求与流式请求。</para>
    /// <para>支持 Session 多轮对话管理。</para>
    /// <para>apiUrl 可传 BaseUrl，也可传完整 /chat/completions 地址。</para>
    /// <para>实例方法可并发调用，但 Dispose 期间不应继续发起请求。</para>
    /// </remarks>
    public sealed class OpenAiCompatibleClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _url;
        private readonly string _key;
        private readonly string _model;
        private readonly SessionStore _sessions = new SessionStore();
        private int _disposed;

        /// <summary>每个 Session 最大保留消息条数，0 表示不保留历史。</summary>
        public int MaxSessionMessages
        {
            get { return _sessions.MaxMessages; }
            set { _sessions.MaxMessages = value; }
        }

        /// <param name="client">
        /// 可选的 <see cref="HttpClient"/> 实例。
        /// 传入 <see langword="null"/> 时使用 <see cref="HttpClients.Default"/>（进程级共享实例）。
        /// 传入的实例生命周期由调用方管理，本类 <see cref="Dispose"/> 不会释放它。
        /// </param>
        public OpenAiCompatibleClient(
            string apiUrl,
            string apiKey,
            string model,
            HttpClient? client = null)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
                throw new ArgumentException("API 地址不能为空白。", nameof(apiUrl));

            if (string.IsNullOrWhiteSpace(apiKey))
                throw new ArgumentException("API Key 不能为空白。", nameof(apiKey));

            if (string.IsNullOrWhiteSpace(model))
                throw new ArgumentException("模型名称不能为空白。", nameof(model));

            _url = NormalizeChatCompletionsUrl(apiUrl);
            _key = apiKey.Trim();
            _model = model.Trim();
            _http = client ?? HttpClients.Default;
        }

        // ── 公共 API ──────────────────────────────────────────────────────────

        /// <summary>发送一次普通对话请求，返回首条回复文本。</summary>
        public async Task<string> ChatAsync(
            string prompt,
            CancellationToken token = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Prompt 不能为空白。", nameof(prompt));

            ChatResponse result = await ChatAsync(new ChatRequest(prompt), token).ConfigureAwait(false);
            return result.Content ?? string.Empty;
        }

        /// <summary>发送一次流式对话请求，按增量片段回调输出。</summary>
        public Task ChatStreamAsync(
            string prompt,
            Action<string> onChunk,
            CancellationToken token = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Prompt 不能为空白。", nameof(prompt));

            return ChatStreamAsync(new ChatRequest(prompt), onChunk, token);
        }

        /// <summary>发送一次普通对话请求，返回完整响应对象。</summary>
        public async Task<ChatResponse> ChatAsync(
            ChatRequest request,
            CancellationToken token = default(CancellationToken))
        {
            Validate(request);
            ThrowIfDisposed();

            IList<object> messages = BuildMessages(request);
            RequestDto dto = BuildDto(request, messages, false);

            using (HttpRequestMessage req = MakeRequest(dto))
            using (HttpResponseMessage resp = await _http
                .SendAsync(req, HttpCompletionOption.ResponseContentRead, token)
                .ConfigureAwait(false))
            {
                string raw = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

                if (!resp.IsSuccessStatusCode)
                    throw MakeException(resp, raw);

                ChatResponse? result = JsonConvert.DeserializeObject<ChatResponse>(raw);

                if (result == null)
                    throw new HttpRequestException("响应反序列化失败。");

                if (ShouldSave(request) && !string.IsNullOrWhiteSpace(result.Content))
                    _sessions.Append(request.SessionId, request.Prompt, result.Content);

                return result;
            }
        }

        /// <summary>发送一次流式对话请求，按增量片段回调输出。</summary>
        public async Task ChatStreamAsync(
            ChatRequest request,
            Action<string> onChunk,
            CancellationToken token = default(CancellationToken))
        {
            if (onChunk == null)
                throw new ArgumentNullException(nameof(onChunk));

            Validate(request);
            ThrowIfDisposed();

            IList<object> messages = BuildMessages(request);
            RequestDto dto = BuildDto(request, messages, true);
            StringBuilder buf = new StringBuilder(512);

            using (HttpRequestMessage req = MakeRequest(dto))
            using (HttpResponseMessage resp = await _http
                .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false))
            {
                if (!resp.IsSuccessStatusCode)
                {
                    string err = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw MakeException(resp, err);
                }

                await SseStreamReader.ReadAsync(
                    resp,
                    payload =>
                    {
                        if (string.IsNullOrWhiteSpace(payload))
                            return;

                        if (string.Equals(payload.Trim(), "[DONE]", StringComparison.OrdinalIgnoreCase))
                            return;

                        OpenAiChatStreamChunk? chunk;

                        try
                        {
                            chunk = JsonConvert.DeserializeObject<OpenAiChatStreamChunk>(payload);
                        }
                        catch (JsonException)
                        {
                            return;
                        }

                        string? text = chunk != null &&
                                      chunk.Choices != null &&
                                      chunk.Choices.Count > 0 &&
                                      chunk.Choices[0] != null &&
                                      chunk.Choices[0].Delta != null
                            ? chunk.Choices[0].Delta.Content
                            : null;

                        if (!string.IsNullOrEmpty(text))
                        {
                            buf.Append(text);
                            onChunk(text);
                        }
                    },
                    token).ConfigureAwait(false);
            }

            if (ShouldSave(request) && buf.Length > 0)
                _sessions.Append(request.SessionId, request.Prompt, buf.ToString());
        }

        /// <summary>返回指定 Session 的历史消息快照，不存在时返回空集合。</summary>
        public IReadOnlyList<ChatMessage> GetHistory(string sessionId)
        {
            return _sessions.Get(sessionId);
        }

        /// <summary>清空指定 Session，返回是否成功移除。</summary>
        public bool ClearSession(string sessionId)
        {
            return _sessions.Remove(sessionId);
        }

        /// <summary>清空全部 Session。</summary>
        public void ClearAllSessions()
        {
            _sessions.Clear();
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
                _sessions.Clear();
        }

        // ── 私有辅助 ──────────────────────────────────────────────────────────

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) == 1)
                throw new ObjectDisposedException(nameof(OpenAiCompatibleClient));
        }

        private static void Validate(ChatRequest r)
        {
            if (r == null)
                throw new ArgumentNullException(nameof(r));

            if (!string.IsNullOrWhiteSpace(r.SessionId) && r.History != null)
                throw new ArgumentException(
                    "不能同时指定 SessionId 与 History,二者语义互斥。", nameof(r));

            if (r.RawMessages != null && r.RawMessages.Count > 0)
                return;

            if (!string.IsNullOrWhiteSpace(r.Prompt))
                return;

            throw new ArgumentException("未提供 Prompt 且 RawMessages 为空。", nameof(r));
        }

        private static bool ShouldSave(ChatRequest r)
        {
            return !string.IsNullOrWhiteSpace(r.SessionId) &&
                   r.RawMessages == null &&
                   !string.IsNullOrWhiteSpace(r.Prompt);
        }

        private IList<object> BuildMessages(ChatRequest r)
        {
            if (r.RawMessages != null)
                return r.RawMessages.ToList();

            List<object> list = new List<object>();

            if (!string.IsNullOrWhiteSpace(r.SystemPrompt))
                list.Add(ChatMessage.System(r.SystemPrompt));

            if (!string.IsNullOrWhiteSpace(r.SessionId))
            {
                list.AddRange(_sessions.Get(r.SessionId).Cast<object>());
            }
            else if (r.History != null)
            {
                list.AddRange(r.History.Cast<object>());
            }

            if (!string.IsNullOrWhiteSpace(r.Prompt))
                list.Add(ChatMessage.User(r.Prompt));

            return list;
        }

        private RequestDto BuildDto(ChatRequest r, IList<object> messages, bool stream)
        {
            RequestDto dto = new RequestDto
            {
                Model = _model,
                Messages = messages,
                Stream = stream,
                MaxTokens = r.MaxTokens > 0 ? r.MaxTokens : (int?)null,
                Temperature = r.Temperature,
                TopP = r.TopP,
                Stop = r.Stop,
                ResponseFormat = r.ResponseFormat
            };

            if (r.ExtraParameters != null && r.ExtraParameters.Count > 0)
            {
                dto.Extra = r.ExtraParameters.ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value == null
                        ? JValue.CreateNull()
                        : JToken.FromObject(kv.Value));
            }

            return dto;
        }

        private HttpRequestMessage MakeRequest(object body)
        {
            HttpRequestMessage req = new HttpRequestMessage(HttpMethod.Post, _url);

            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _key);

            req.Content = new StringContent(
                JsonConvert.SerializeObject(body, Formatting.None),
                Encoding.UTF8,
                "application/json");

            return req;
        }

        /// <summary>
        /// 规范化 Chat Completions 地址。
        /// </summary>
        /// <remarks>
        /// 支持：
        /// https://api.deepseek.com
        /// https://api.deepseek.com/chat/completions
        /// https://api.openai.com/v1
        /// https://api.openai.com/v1/chat/completions
        /// </remarks>
        private static string NormalizeChatCompletionsUrl(string apiUrl)
        {
            if (string.IsNullOrWhiteSpace(apiUrl))
                throw new ArgumentException("API 地址不能为空白。", nameof(apiUrl));

            string url = apiUrl.Trim();

            while (url.EndsWith("/", StringComparison.Ordinal))
                url = url.Substring(0, url.Length - 1);

            if (url.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return url;

            return url + "/chat/completions";
        }

        private static HttpRequestException MakeException(HttpResponseMessage r, string body)
        {
            return new HttpRequestException(
                "[" + (int)r.StatusCode + "] " +
                r.ReasonPhrase +
                Environment.NewLine +
                body);
        }

        // ── 私有嵌套：请求 DTO ────────────────────────────────────────────────

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class RequestDto
        {
            [JsonProperty("model")]
            public string Model { get; set; } = null!;

            [JsonProperty("messages")]
            public IList<object> Messages { get; set; } = null!;

            [JsonProperty("stream")]
            public bool Stream { get; set; }

            [JsonProperty("max_tokens", NullValueHandling = NullValueHandling.Ignore)]
            public int? MaxTokens { get; set; }

            [JsonProperty("temperature", NullValueHandling = NullValueHandling.Ignore)]
            public double? Temperature { get; set; }

            [JsonProperty("top_p", NullValueHandling = NullValueHandling.Ignore)]
            public double? TopP { get; set; }

            [JsonProperty("stop", NullValueHandling = NullValueHandling.Ignore)]
            public object? Stop { get; set; }

            [JsonProperty("response_format", NullValueHandling = NullValueHandling.Ignore)]
            public object? ResponseFormat { get; set; }

            [JsonExtensionData]
            public Dictionary<string, JToken>? Extra { get; set; }
        }

        // ── 私有嵌套：Session 存储 ────────────────────────────────────────────

        private sealed class SessionStore
        {
            private readonly ConcurrentDictionary<string, List<ChatMessage>> _store =
                new ConcurrentDictionary<string, List<ChatMessage>>();

            private int _max = 50;

            public int MaxMessages
            {
                get { return Volatile.Read(ref _max); }
                set
                {
                    if (value < 0)
                        throw new ArgumentOutOfRangeException(nameof(value), "不能小于 0。");

                    Volatile.Write(ref _max, value);
                }
            }

            public IReadOnlyList<ChatMessage> Get(string id)
            {
                if (string.IsNullOrWhiteSpace(id))
                    return Array.Empty<ChatMessage>();

                List<ChatMessage>? h;

                if (_store.TryGetValue(id, out h))
                {
                    lock (h)
                    {
                        return h.ToArray();
                    }
                }

                return Array.Empty<ChatMessage>();
            }

            public void Append(string? id, string? user, string? assistant)
            {
                if (string.IsNullOrWhiteSpace(id) || MaxMessages <= 0)
                    return;

                List<ChatMessage> h = _store.GetOrAdd(id, _ => new List<ChatMessage>());

                lock (h)
                {
                    h.Add(ChatMessage.User(user!));
                    h.Add(ChatMessage.Assistant(assistant!));

                    int limit = MaxMessages;

                    while (h.Count > limit && h.Count >= 2)
                    {
                        h.RemoveAt(0);
                        h.RemoveAt(0);
                    }
                }
            }

            public bool Remove(string id)
            {
                List<ChatMessage>? removed;
                return !string.IsNullOrWhiteSpace(id) &&
                       _store.TryRemove(id, out removed);
            }

            public void Clear()
            {
                _store.Clear();
            }
        }
    }
}