using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using KkjQuicker.Utilities.Win32;

namespace KkjQuicker.Utilities.Input
{
    /// <summary>
    /// 输入法（IME）状态查询与控制工具。
    /// 跨进程通过 WM_IME_CONTROL + GUITHREADINFO.hwndFocus 路径定位 IME 窗口，
    /// 使用 SendMessageTimeout 避免目标线程卡顿导致的死锁。
    /// </summary>
    /// <remarks>
    /// 默认目标为前台窗口（hwnd = IntPtr.Zero）。所有 Set/Push 方法的返回值
    /// 仅表示"是否成功定位到 IME 窗口并发送了消息"，不保证状态实际生效。
    /// 对生效有强依赖的调用方，应在切换后调用 <see cref="IsChineseMode"/>
    /// 等查询方法复查。
    ///
    /// 已知限制：UWP 应用、远程桌面、部分游戏窗口可能不响应 WM_IME_CONTROL，
    /// 此时 Set 调用返回 true 但实际无效；Push/With 在这种情况下行为退化为透明，
    /// 不抛异常。
    /// </remarks>
    public static class ImeHelper
    {
        /// <summary>
        /// 状态切换后等待 IME 实际生效的延迟（毫秒）。默认 30ms。
        /// 切换 IME 状态后立刻发送字符，前几个字符可能用旧状态处理；
        /// 多数 IME 在 20~50ms 内完成切换。
        /// </summary>
        public static int StateChangeDelayMs { get; set; } = 30;

        /// <summary>
        /// 跨进程 SendMessage 的超时（毫秒）。默认 200ms。
        /// </summary>
        public static uint SendMessageTimeoutMs { get; set; } = 200;

        // ============================================================
        // 状态查询
        // ============================================================

        /// <summary>
        /// 尝试获取目标窗口的 IME 状态。
        /// </summary>
        /// <returns>
        /// true 表示成功定位 IME 并读取了状态；
        /// false 表示定位失败或读取超时（state 为 default(ImeState)）。
        /// </returns>
        public static bool TryGetState(out ImeState state, IntPtr hwnd = default(IntPtr))
        {
            IntPtr imeWnd = ResolveImeWindow(hwnd);
            if (imeWnd == IntPtr.Zero)
            {
                state = default(ImeState);
                return false;
            }

            IntPtr openResult, modeResult;
            if (!TrySendImeMessage(imeWnd, NativeMethods.IMC_GETOPENSTATUS, IntPtr.Zero, out openResult) ||
                !TrySendImeMessage(imeWnd, NativeMethods.IMC_GETCONVERSIONMODE, IntPtr.Zero, out modeResult))
            {
                state = default(ImeState);
                return false;
            }

            state = new ImeState(openResult != IntPtr.Zero, modeResult.ToInt32());
            return true;
        }

        /// <summary>
        /// 获取目标窗口的 IME 状态。定位失败时返回 default(ImeState)。
        /// 需要区分"读取失败"和"真实英文状态"时改用 <see cref="TryGetState"/>。
        /// </summary>
        public static ImeState GetState(IntPtr hwnd = default(IntPtr))
        {
            ImeState state;
            TryGetState(out state, hwnd);
            return state;
        }

        public static bool IsImeOpen(IntPtr hwnd = default(IntPtr))
        {
            return GetState(hwnd).IsOpen;
        }

        public static bool IsChineseMode(IntPtr hwnd = default(IntPtr))
        {
            return GetState(hwnd).IsChinese;
        }

        public static bool IsFullShape(IntPtr hwnd = default(IntPtr))
        {
            return GetState(hwnd).IsFullShape;
        }

        // ============================================================
        // 状态设置
        // ============================================================

        /// <summary>
        /// 设置 IME 开关。返回是否成功定位到 IME 窗口并发送消息（不保证实际生效）。
        /// </summary>
        public static bool SetImeOpen(bool open, IntPtr hwnd = default(IntPtr))
        {
            IntPtr imeWnd = ResolveImeWindow(hwnd);
            if (imeWnd == IntPtr.Zero)
                return false;

            SendImeMessage(imeWnd, NativeMethods.IMC_SETOPENSTATUS,
                open ? (IntPtr)1 : IntPtr.Zero);
            return true;
        }

        /// <summary>
        /// 切换中/英文模式。
        /// 中文 = 开 OpenStatus 并设 ConversionMode = NATIVE | SYMBOL；
        /// 英文 = 关 OpenStatus 并清空 ConversionMode。
        /// 注意"英文"语义等价于"关闭 IME"，与 Quicker 内部处理方式一致。
        /// </summary>
        public static bool SetChineseMode(bool chinese, IntPtr hwnd = default(IntPtr))
        {
            IntPtr imeWnd = ResolveImeWindow(hwnd);
            if (imeWnd == IntPtr.Zero)
                return false;

            SendImeMessage(imeWnd, NativeMethods.IMC_SETOPENSTATUS,
                chinese ? (IntPtr)1 : IntPtr.Zero);

            int mode = chinese
                ? (NativeMethods.IME_CMODE_NATIVE | NativeMethods.IME_CMODE_SYMBOL)
                : 0;
            SendImeMessage(imeWnd, NativeMethods.IMC_SETCONVERSIONMODE, (IntPtr)mode);

            return true;
        }

        /// <summary>
        /// 还原到指定状态（精确还原所有 ConversionMode 位）。
        /// </summary>
        public static bool ApplyState(ImeState state, IntPtr hwnd = default(IntPtr))
        {
            IntPtr imeWnd = ResolveImeWindow(hwnd);
            if (imeWnd == IntPtr.Zero)
                return false;

            SendImeMessage(imeWnd, NativeMethods.IMC_SETOPENSTATUS,
                state.IsOpen ? (IntPtr)1 : IntPtr.Zero);
            SendImeMessage(imeWnd, NativeMethods.IMC_SETCONVERSIONMODE,
                (IntPtr)state.ConversionMode);

            return true;
        }

        // ============================================================
        // 作用域 API（推荐使用）
        // ============================================================

        /// <summary>
        /// 进入"IME 关闭"作用域，离开 using 块自动还原原状态。
        /// 还原绑定到进入作用域时的窗口，不跟随前台变化。
        /// </summary>
        public static IDisposable PushImeOff(IntPtr hwnd = default(IntPtr))
        {
            IntPtr resolved = (hwnd == IntPtr.Zero) ? NativeMethods.GetForegroundWindow() : hwnd;
            ImeState prev;
            if (!TryGetState(out prev, resolved) || !SetImeOpen(false, resolved))
                return NoOpScope.Instance;

            SleepForStateChange();
            return new ImeStateScope(resolved, prev);
        }

        /// <summary>
        /// 进入"英文模式"作用域，离开自动还原。
        /// </summary>
        public static IDisposable PushEnglishMode(IntPtr hwnd = default(IntPtr))
        {
            IntPtr resolved = (hwnd == IntPtr.Zero) ? NativeMethods.GetForegroundWindow() : hwnd;
            ImeState prev;
            if (!TryGetState(out prev, resolved) || !SetChineseMode(false, resolved))
                return NoOpScope.Instance;

            SleepForStateChange();
            return new ImeStateScope(resolved, prev);
        }

        /// <summary>
        /// 进入指定状态作用域，离开自动还原。
        /// </summary>
        public static IDisposable PushState(ImeState target, IntPtr hwnd = default(IntPtr))
        {
            IntPtr resolved = (hwnd == IntPtr.Zero) ? NativeMethods.GetForegroundWindow() : hwnd;
            ImeState prev;
            if (!TryGetState(out prev, resolved) || !ApplyState(target, resolved))
                return NoOpScope.Instance;

            SleepForStateChange();
            return new ImeStateScope(resolved, prev);
        }

        /// <summary>
        /// 在 IME 关闭状态下执行同步操作，结束自动还原。
        /// </summary>
        public static void WithImeOff(Action action, IntPtr hwnd = default(IntPtr))
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            using (PushImeOff(hwnd))
            {
                action();
            }
        }

        /// <summary>
        /// 在 IME 关闭状态下执行同步操作并返回结果。
        /// </summary>
        public static T WithImeOff<T>(Func<T> func, IntPtr hwnd = default(IntPtr))
        {
            if (func == null) throw new ArgumentNullException(nameof(func));
            using (PushImeOff(hwnd))
            {
                return func();
            }
        }

        /// <summary>
        /// 在 IME 关闭状态下执行异步操作，结束自动还原。
        /// 注意：异步过程中前台窗口可能变化，但还原仍作用于进入时记录的窗口。
        /// </summary>
        public static async Task WithImeOffAsync(Func<Task> action, IntPtr hwnd = default(IntPtr))
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            IDisposable scope = PushImeOff(hwnd);
            try
            {
                await action().ConfigureAwait(false);
            }
            finally
            {
                scope.Dispose();
            }
        }

        // ============================================================
        // 内部实现
        // ============================================================

        /// <summary>
        /// 定位目标窗口对应的 IME 窗口。
        /// 路径：hwnd（或前台窗口） → 线程ID → GUITHREADINFO.hwndFocus → ImmGetDefaultIMEWnd。
        /// </summary>
        private static IntPtr ResolveImeWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                hwnd = NativeMethods.GetForegroundWindow();
                if (hwnd == IntPtr.Zero)
                    return IntPtr.Zero;
            }

            // 注意：函数返回值是 threadId，out 参数才是 processId。
            uint processId;
            uint threadId = NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
            if (threadId == 0)
                return IntPtr.Zero;

            GUITHREADINFO info = new GUITHREADINFO();
            info.cbSize = Marshal.SizeOf(typeof(GUITHREADINFO));

            IntPtr focusHandle;
            if (NativeMethods.GetGUIThreadInfo(threadId, ref info) && info.hwndFocus != IntPtr.Zero)
            {
                focusHandle = info.hwndFocus;
            }
            else
            {
                // 拿不到焦点子控件，退化为顶层窗口
                focusHandle = hwnd;
            }

            return NativeMethods.ImmGetDefaultIMEWnd(focusHandle);
        }

        /// <summary>
        /// 用 SendMessageTimeout 发送 WM_IME_CONTROL，避免目标线程卡顿导致死锁。
        /// 超时返回 false，调用方可据此区分"读取失败"和"消息结果为 0"。
        /// </summary>
        private static bool TrySendImeMessage(IntPtr imeWnd, int controlCode, IntPtr lParam, out IntPtr result)
        {
            IntPtr ret = NativeMethods.SendMessageTimeout(
                imeWnd,
                NativeMethods.WM_IME_CONTROL,
                (IntPtr)controlCode,
                lParam,
                NativeMethods.SMTO_ABORTIFHUNG,
                SendMessageTimeoutMs,
                out result);

            if (ret == IntPtr.Zero)
            {
                Trace.WriteLine(string.Format(
                    "[ImeHelper] SendMessageTimeout 失败或超时. controlCode=0x{0:X4}",
                    controlCode));
                result = IntPtr.Zero;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 发送 WM_IME_CONTROL 消息，超时或失败时静默返回 IntPtr.Zero。
        /// 仅用于不关心读取结果是否真实有效的写入场景（Set/Apply）。
        /// </summary>
        private static IntPtr SendImeMessage(IntPtr imeWnd, int controlCode, IntPtr lParam)
        {
            IntPtr result;
            TrySendImeMessage(imeWnd, controlCode, lParam, out result);
            return result;
        }

        private static void SleepForStateChange()
        {
            int ms = StateChangeDelayMs;
            if (ms > 0) Thread.Sleep(ms);
        }

        /// <summary>
        /// 真正承载状态还原的作用域。Dispose 幂等。
        /// </summary>
        private sealed class ImeStateScope : IDisposable
        {
            private readonly IntPtr _hwnd;
            private readonly ImeState _previous;
            private int _disposed;

            public ImeStateScope(IntPtr hwnd, ImeState previous)
            {
                _hwnd = hwnd;
                _previous = previous;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
                try
                {
                    if (!ApplyState(_previous, _hwnd))
                    {
                        Trace.WriteLine("[ImeHelper] 还原 IME 状态失败：定位 IME 窗口失败（窗口可能已关闭）");
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("[ImeHelper] 还原 IME 状态异常: " + ex.Message);
                }
            }
        }

        /// <summary>
        /// 定位 IME 窗口失败时返回的空作用域。
        /// </summary>
        private sealed class NoOpScope : IDisposable
        {
            public static readonly NoOpScope Instance = new NoOpScope();
            private NoOpScope() { }
            public void Dispose() { }
        }
    }

    /// <summary>
    /// IME 状态快照。保留原始 ConversionMode 位，便于精确还原。
    /// </summary>
    public readonly struct ImeState : IEquatable<ImeState>
    {
        public bool IsOpen { get; }
        public int ConversionMode { get; }

        public ImeState(bool isOpen, int conversionMode)
        {
            IsOpen = isOpen;
            ConversionMode = conversionMode;
        }

        /// <summary>是否中文输入模式（IME 打开 AND ConversionMode 含 NATIVE 位）。</summary>
        public bool IsChinese
        {
            get { return IsOpen && (ConversionMode & NativeMethods.IME_CMODE_NATIVE) != 0; }
        }

        /// <summary>是否全角。</summary>
        public bool IsFullShape
        {
            get { return (ConversionMode & NativeMethods.IME_CMODE_FULLSHAPE) != 0; }
        }

        /// <summary>是否中文标点（SYMBOL 位）。</summary>
        public bool IsChinesePunctuation
        {
            get { return (ConversionMode & NativeMethods.IME_CMODE_SYMBOL) != 0; }
        }

        public bool Equals(ImeState other)
        {
            return IsOpen == other.IsOpen && ConversionMode == other.ConversionMode;
        }

        public override bool Equals(object obj)
        {
            return obj is ImeState && Equals((ImeState)obj);
        }

        public override int GetHashCode()
        {
            return (IsOpen.GetHashCode() * 397) ^ ConversionMode;
        }

        public override string ToString()
        {
            return string.Format(
                "ImeState(Open={0}, Chinese={1}, FullShape={2}, ChinesePunct={3})",
                IsOpen, IsChinese, IsFullShape, IsChinesePunctuation);
        }
    }
}