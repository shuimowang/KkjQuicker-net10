ReliableWebSocketClient = 带自动重连 + 心跳 + 接收循环的“高层 WebSocket 客户端实现”

KkjQuicker.Net.Http
├─ IHttpClient.cs             // HTTP 便利接口（可选）
├─ HttpClients.cs             // HttpClient 单例工厂
├─ HttpJsonExtensions.cs      // JSON 扩展方法
├─ RetryPolicy.cs             // 通用指数退避重试
├─ ApiClientBase.cs           // API 客户端基类
└─ Streaming
   └─ SseStreamReader.cs      // SSE / LLM 流式读取