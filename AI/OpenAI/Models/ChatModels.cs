using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace KkjQuicker.AI.OpenAI.Models
{
    // ── ChatMessage ───────────────────────────────────────────────────────────

    /// <summary>
    /// 表示一条对话消息，不可变，通过静态工厂方法创建。
    /// </summary>
    [JsonConverter(typeof(ChatMessageJsonConverter))]
    public sealed class ChatMessage
    {
        /// <summary>消息角色，常见值为 user / assistant / system。</summary>
        public string Role { get; }

        /// <summary>消息正文。</summary>
        public string Content { get; }

        private ChatMessage(string role, string? content)
        {
            if (string.IsNullOrWhiteSpace(role))
                throw new ArgumentException("Role 不能为空白。", nameof(role));

            Role = role;
            Content = content ?? string.Empty;
        }

        public static ChatMessage User(string content)
        {
            return new ChatMessage("user", content);
        }

        public static ChatMessage Assistant(string content)
        {
            return new ChatMessage("assistant", content);
        }

        public static ChatMessage System(string content)
        {
            return new ChatMessage("system", content);
        }

        /// <summary>仅供反序列化使用,对缺失的 role 给出兜底默认值,避免污染调用方异常路径。</summary>
        internal static ChatMessage FromDeserialization(string? role, string? content)
        {
            return new ChatMessage(
                string.IsNullOrWhiteSpace(role) ? "assistant" : role,
                content);
        }
    }

    internal sealed class ChatMessageJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(ChatMessage);
        }

        public override object? ReadJson(
            JsonReader reader,
            Type objectType,
            object? existingValue,
            JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            string? role = null;
            string? content = null;

            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.EndObject)
                    break;

                if (reader.TokenType != JsonToken.PropertyName)
                    continue;

                string? name = reader.Value as string;
                reader.Read();

                if (string.Equals(name, "role", StringComparison.OrdinalIgnoreCase))
                {
                    role = reader.Value == null ? null : reader.Value.ToString();
                }
                else if (string.Equals(name, "content", StringComparison.OrdinalIgnoreCase))
                {
                    content = reader.Value == null ? null : reader.Value.ToString();
                }
                else if (reader.TokenType == JsonToken.StartObject ||
                         reader.TokenType == JsonToken.StartArray)
                {
                    reader.Skip();
                }
            }

            return ChatMessage.FromDeserialization(role, content);
        }

        public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
        {
            ChatMessage m = (ChatMessage)value!;

            writer.WriteStartObject();

            writer.WritePropertyName("role");
            writer.WriteValue(m.Role);

            writer.WritePropertyName("content");
            writer.WriteValue(m.Content);

            writer.WriteEndObject();
        }
    }

    // ── ChatResponse ──────────────────────────────────────────────────────────

    /// <summary>表示非流式 Chat Completions 响应。</summary>
    public sealed class ChatResponse
    {
        [JsonProperty("id")]
        public string? Id { get; set; }

        [JsonProperty("choices")]
        public List<ChatResponseChoice>? Choices { get; set; }

        [JsonProperty("usage")]
        public TokenUsage? Usage { get; set; }

        /// <summary>快捷获取首条回复文本，无有效消息时返回 null。</summary>
        public string? Content
        {
            get
            {
                return Choices != null && Choices.Count > 0
                    ? Choices[0]?.Message?.Content
                    : null;
            }
        }
    }

    public sealed class ChatResponseChoice
    {
        [JsonProperty("message")]
        public ChatMessage? Message { get; set; }

        [JsonProperty("finish_reason")]
        public string? FinishReason { get; set; }
    }

    public sealed class TokenUsage
    {
        [JsonProperty("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonProperty("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }
    }

    // ── 流式响应 Chunk（内部用）───────────────────────────────────────────────

    internal sealed class OpenAiChatStreamChunk
    {
        [JsonProperty("choices")]
        public List<StreamChoice>? Choices { get; set; }
    }

    internal sealed class StreamChoice
    {
        [JsonProperty("delta")]
        public StreamDelta? Delta { get; set; }

        [JsonProperty("finish_reason")]
        public string? FinishReason { get; set; }
    }

    internal sealed class StreamDelta
    {
        [JsonProperty("content")]
        public string? Content { get; set; }

        // 一些兼容服务可能会返回 reasoning_content。
        // 当前客户端仍只把 content 作为正式回复输出。
        [JsonProperty("reasoning_content")]
        public string? ReasoningContent { get; set; }
    }
}
