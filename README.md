# KkjQuicker-net10

KkjQuicker 核心类库，基于 .NET 10.0 + WPF，提供桌面应用常用的基础能力封装。

## 目标框架

- `net10.0-windows`
- WPF + Windows Forms
- x64 专属

## 功能模块

### AI

| 模块 | 说明 |
|------|------|
| `AI.OpenAI` | 兼容 OpenAI Chat Completions 协议的客户端，支持普通请求与流式请求、Session 多轮对话管理 |
| `AI.ASR.AliyunRealtime` | 阿里百炼 / DashScope 实时语音识别客户端，基于 WebSocket 协议，支持服务端 VAD、中间文本与最终文本回调 |

### Audio

| 模块 | 说明 |
|------|------|
| `Audio.Sources.LoopbackAudioSource` | 系统回环音频采集（What U Hear），输出 16kHz/16bit/Mono PCM，支持多声道（5.1/7.1） |
| `Audio.Sources.MicrophoneAudioSource` | 麦克风音频采集 |
| `Audio.AudioDeviceHelper` | 音频设备枚举辅助 |

### Domain

| 模块 | 说明 |
|------|------|
| `Domain.AppState` | 应用全局状态管理 |
| `Domain.ClipboardHistory` | 剪贴板历史记录管理 |

### Net

| 模块 | 说明 |
|------|------|
| `Net.Http.HttpClients` | 进程级 HttpClient 单例与独立实例创建 |
| `Net.Http.SseStreamReader` | SSE（Server-Sent Events）流式读取 |
| `Net.WebSockets.ReliableWebSocketClient` | 带接收循环与自动重连的 WebSocket 客户端，支持指数退避 + 随机抖动 |

### Overlay

| 模块 | 说明 |
|------|------|
| `Overlay.Engine` | 覆盖层总控引擎，管理图层生命周期、Z 顺序、输入路由与捕获 |
| `Overlay.Layers` | 内置图层：截图编辑器、截图标注、呼吸边框、水波纹 |
| `Overlay.Docking` | 窗口停靠引擎 |
| `Overlay.Applications` | 侧边栏宿主 |

### UI

| 模块 | 说明 |
|------|------|
| `UI.Hotkeys` | 全局热键（RegisterHotKey）与低级键盘 Hook |
| `UI.Controls` | 图标控件、虚拟化换行面板 |
| `UI.Converters` | 常用值转换器（Bool、颜色、相等、索引、相对时间等） |
| `UI.Behaviors` | 多选行为、事件-布尔绑定器 |
| `UI.Adorners` | 拖拽装饰器、叠加装饰器 |
| `UI.AnimationHelper` | 动画辅助 |
| `UI.ViewModelBase` | MVVM 基类 |

### Utilities

| 模块 | 说明 |
|------|------|
| `Utilities.Hooks` | 全局键盘/鼠标低级 Hook、全局热键 |
| `Utilities.Win32` | Win32 P/Invoke 声明、窗口辅助、剪贴板监听、前台窗口监控 |
| `Utilities.Imaging` | 屏幕截图、DPI 辅助、图片处理、二维码、文字贴纸渲染 |
| `Utilities.FFmpeg` | FFmpeg 进程执行、优雅停止、命令行构建 |
| `Utilities.Threading` | 防抖/节流控制器（ActionRateLimiter）、动作队列限制器 |
| `Utilities.Extensions` | 字符串、列表、字典、枚举、JSON 扩展方法 |
| `Utilities.Input` | 输入模拟、IME 辅助 |
| `Utilities.FileSystem` | 文件系统辅助 |
| `Utilities.History` | 通用历史记录管理器 |

## 依赖

| 包 | 用途 |
|----|------|
| NAudio | 音频采集与处理 |
| Newtonsoft.Json | JSON 序列化 |
| QRCoder | 二维码生成 |
| System.Drawing.Common | GDI+ 图像处理 |
| InputSimulatorCore | 输入模拟 |
| ZXing.Net | 二维码/条码识别 |
| FontAwesome5 | 图标字体 |

## 构建

```bash
dotnet build KkjQuicker.csproj
```

要求：.NET 10 SDK，Windows x64。
