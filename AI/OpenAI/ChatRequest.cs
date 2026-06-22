using System;
using System.Collections.Generic;
using System.Linq;
using KkjQuicker.AI.OpenAI.Models;

namespace KkjQuicker.AI.OpenAI
{
    /// <summary>
    /// 对话请求构建器，Fluent 风格，非线程安全。
    /// </summary>
    public sealed class ChatRequest
    {
        private static readonly HashSet<string> ReservedExtraKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "model",
                "messages",
                "stream",
                "max_tokens",
                "temperature",
                "top_p",
                "stop",
                "response_format"
            };

        public string Prompt { get; private set; }
        public string SystemPrompt { get; private set; }
        public string SessionId { get; private set; }
        public IList<ChatMessage> History { get; private set; }
        public IList<object> RawMessages { get; private set; }
        public IDictionary<string, object> ExtraParameters { get; private set; }

        /// <summary>0 表示不发送 max_tokens 参数。</summary>
        public int MaxTokens { get; private set; }

        public double? Temperature { get; private set; }
        public double? TopP { get; private set; }

        /// <summary>stop 可为 string 或 string[]。</summary>
        public object Stop { get; private set; }

        public object ResponseFormat { get; private set; }
        public bool Stream { get; private set; }

        public ChatRequest(string prompt = null)
        {
            Prompt = Normalize(prompt);
        }

        public ChatRequest WithSystem(string v)
        {
            SystemPrompt = Normalize(v);
            return this;
        }

        public ChatRequest WithSession(string v)
        {
            SessionId = Normalize(v);
            return this;
        }

        public ChatRequest UseStream()
        {
            Stream = true;
            return this;
        }

        public ChatRequest WithStop(string v)
        {
            Stop = Normalize(v);
            return this;
        }

        public ChatRequest WithStop(IEnumerable<string> values)
        {
            if (values == null)
            {
                Stop = null;
                return this;
            }

            string[] arr = values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToArray();

            Stop = arr.Length > 0 ? (object)arr : null;
            return this;
        }

        public ChatRequest WithTemperature(double v)
        {
            if (v < 0 || v > 2)
                throw new ArgumentOutOfRangeException(nameof(v), "Temperature 范围为 0~2。");

            Temperature = v;
            return this;
        }

        public ChatRequest WithTopP(double v)
        {
            if (v < 0 || v > 1)
                throw new ArgumentOutOfRangeException(nameof(v), "TopP 范围为 0~1。");

            TopP = v;
            return this;
        }

        public ChatRequest WithMaxTokens(int v)
        {
            if (v < 0)
                throw new ArgumentOutOfRangeException(nameof(v), "MaxTokens 不能小于 0，0 表示不发送该参数。");

            MaxTokens = v;
            return this;
        }

        public ChatRequest WithHistory(IEnumerable<ChatMessage> history)
        {
            History = history == null ? null : history.ToList();
            return this;
        }

        public ChatRequest WithRawMessages(IEnumerable<object> messages)
        {
            RawMessages = messages == null ? null : messages.ToList();
            return this;
        }

        public ChatRequest WithResponseFormat(string type)
        {
            string t = Normalize(type);

            ResponseFormat = t == null
                ? null
                : (object)new Dictionary<string, object> { { "type", t } };

            return this;
        }

        public ChatRequest WithResponseFormat(object format)
        {
            ResponseFormat = format;
            return this;
        }

        public ChatRequest WithExtra(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("key 不能为空白。", nameof(key));

            key = key.Trim();

            if (ReservedExtraKeys.Contains(key))
                throw new ArgumentException("不能通过 ExtraParameters 覆盖基础请求字段：" + key, nameof(key));

            if (ExtraParameters == null)
                ExtraParameters = new Dictionary<string, object>();

            ExtraParameters[key] = value;
            return this;
        }

        private static string Normalize(string s)
        {
            return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        }
    }
}