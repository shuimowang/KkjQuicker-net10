using System;
using System.Collections.Generic;

namespace KkjQuicker.Utilities.History
{
    /// <summary>
    /// 表示一条可撤销 / 可重做的命令。
    /// </summary>
    public interface IUndoableCommand
    {
        /// <summary>
        /// 获取命令描述。
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 执行撤销。
        /// </summary>
        void Undo();

        /// <summary>
        /// 执行重做。
        /// </summary>
        void Redo();
    }

    /// <summary>
    /// 表示历史记录变更类型。
    /// </summary>
    public enum HistoryChangeKind
    {
        /// <summary>
        /// 未知变化。
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// 直接压入了一条历史记录。
        /// </summary>
        Pushed = 1,

        /// <summary>
        /// 执行并记录了一条历史命令。
        /// </summary>
        Executed = 2,

        /// <summary>
        /// 执行了撤销。
        /// </summary>
        Undone = 3,

        /// <summary>
        /// 执行了重做。
        /// </summary>
        Redone = 4,

        /// <summary>
        /// 清空了历史记录。
        /// </summary>
        Cleared = 5,

        /// <summary>
        /// 最大历史容量发生了变化。
        /// </summary>
        CapacityChanged = 6,

        /// <summary>
        /// 调用了 <see cref="HistoryManager.MarkClean"/>，存档点已更新。
        /// </summary>
        MarkedClean = 7,
    }

    /// <summary>
    /// 表示当前历史管理器的状态快照。
    /// </summary>
    public sealed class HistoryState
    {
        /// <summary>
        /// 初始化一个 <see cref="HistoryState"/> 实例。
        /// </summary>
        /// <param name="canUndo">是否可撤销。</param>
        /// <param name="canRedo">是否可重做。</param>
        /// <param name="undoCount">撤销栈数量。</param>
        /// <param name="redoCount">重做栈数量。</param>
        /// <param name="undoDescription">下一次撤销的命令描述。</param>
        /// <param name="redoDescription">下一次重做的命令描述。</param>
        /// <param name="isDirty">自上次 <see cref="HistoryManager.MarkClean"/> 后是否有未保存的改动。</param>
        public HistoryState(
            bool canUndo,
            bool canRedo,
            int undoCount,
            int redoCount,
            string undoDescription,
            string redoDescription,
            bool isDirty)
        {
            CanUndo = canUndo;
            CanRedo = canRedo;
            UndoCount = undoCount;
            RedoCount = redoCount;
            UndoDescription = undoDescription ?? string.Empty;
            RedoDescription = redoDescription ?? string.Empty;
            IsDirty = isDirty;
        }

        /// <summary>
        /// 获取当前是否可撤销。
        /// </summary>
        public bool CanUndo { get; }

        /// <summary>
        /// 获取当前是否可重做。
        /// </summary>
        public bool CanRedo { get; }

        /// <summary>
        /// 获取撤销栈数量。
        /// </summary>
        public int UndoCount { get; }

        /// <summary>
        /// 获取重做栈数量。
        /// </summary>
        public int RedoCount { get; }

        /// <summary>
        /// 获取下一次撤销的命令描述。
        /// </summary>
        public string UndoDescription { get; }

        /// <summary>
        /// 获取下一次重做的命令描述。
        /// </summary>
        public string RedoDescription { get; }

        /// <summary>
        /// 获取自上次 <see cref="HistoryManager.MarkClean"/> 后是否有未保存的改动。
        /// </summary>
        public bool IsDirty { get; }
    }

    /// <summary>
    /// 表示历史变化事件参数。
    /// </summary>
    public sealed class HistoryChangedEventArgs : EventArgs
    {
        /// <summary>
        /// 初始化一个 <see cref="HistoryChangedEventArgs"/> 实例。
        /// </summary>
        /// <param name="kind">变化类型。</param>
        /// <param name="state">变化后的状态快照。</param>
        public HistoryChangedEventArgs(HistoryChangeKind kind, HistoryState state)
        {
            Kind = kind;
            State = state;
        }

        /// <summary>
        /// 获取变化类型。
        /// </summary>
        public HistoryChangeKind Kind { get; }

        /// <summary>
        /// 获取变化后的状态快照。
        /// </summary>
        public HistoryState State { get; }
    }

    /// <summary>
    /// 基于委托的轻量命令实现。
    /// </summary>
    public sealed class DelegateUndoableCommand : IUndoableCommand
    {
        private readonly Action _undo;
        private readonly Action _redo;
        private readonly string _description;

        /// <summary>
        /// 初始化一个 <see cref="DelegateUndoableCommand"/> 实例。
        /// </summary>
        /// <param name="description">命令描述。</param>
        /// <param name="undo">撤销动作。</param>
        /// <param name="redo">重做动作。</param>
        public DelegateUndoableCommand(string description, Action undo, Action redo)
        {
            if (undo == null)
                throw new ArgumentNullException(nameof(undo));
            if (redo == null)
                throw new ArgumentNullException(nameof(redo));

            _description = description ?? string.Empty;
            _undo = undo;
            _redo = redo;
        }

        /// <summary>
        /// 获取命令描述。
        /// </summary>
        public string Description
        {
            get { return _description; }
        }

        /// <summary>
        /// 执行撤销。
        /// </summary>
        public void Undo()
        {
            _undo();
        }

        /// <summary>
        /// 执行重做。
        /// </summary>
        public void Redo()
        {
            _redo();
        }
    }

    /// <summary>
    /// 复合命令，用于将多条命令组合成一次撤销 / 重做。
    /// </summary>
    public sealed class CompositeUndoableCommand : IUndoableCommand
    {
        private readonly List<IUndoableCommand> _commands = new List<IUndoableCommand>();
        private readonly string _description;

        /// <summary>
        /// 初始化一个 <see cref="CompositeUndoableCommand"/> 实例。
        /// </summary>
        /// <param name="description">复合命令描述。</param>
        public CompositeUndoableCommand(string description)
        {
            _description = description ?? string.Empty;
        }

        /// <summary>
        /// 获取命令描述。
        /// </summary>
        public string Description
        {
            get { return _description; }
        }

        /// <summary>
        /// 获取当前包含的子命令数量。
        /// </summary>
        public int Count
        {
            get { return _commands.Count; }
        }

        /// <summary>
        /// 获取当前包含的子命令集合。
        /// </summary>
        public IReadOnlyList<IUndoableCommand> Commands
        {
            get { return _commands; }
        }

        /// <summary>
        /// 添加一个子命令。
        /// </summary>
        /// <param name="command">要添加的命令。</param>
        public void Add(IUndoableCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            _commands.Add(command);
        }

        /// <summary>
        /// 按逆序执行所有子命令的撤销。
        /// </summary>
        public void Undo()
        {
            for (int i = _commands.Count - 1; i >= 0; i--)
            {
                _commands[i].Undo();
            }
        }

        /// <summary>
        /// 按顺序执行所有子命令的重做。
        /// </summary>
        public void Redo()
        {
            for (int i = 0; i < _commands.Count; i++)
            {
                _commands[i].Redo();
            }
        }
    }

    /// <summary>
    /// 轻量撤销 / 重做管理器。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 该类型用于管理普通撤销 / 重做场景，不承担批处理事务或命令自动合并等高级职责。
    /// </para>
    /// <para>
    /// 如需将多步操作合并为一步历史，请由调用方显式构造 <see cref="CompositeUndoableCommand"/>。
    /// </para>
    /// </remarks>
    public sealed class HistoryManager : IDisposable
    {
        private readonly List<IUndoableCommand> _undoStack = new List<IUndoableCommand>();
        private readonly Stack<IUndoableCommand> _redoStack = new Stack<IUndoableCommand>();
        private bool _isDisposed;
        private int _maxCapacity = 100;

        // 存档点：记录上次 MarkClean 时的撤销栈深度。
        // 负值表示存档点已因容量裁剪而丢失，此时永远为 dirty。
        private int _cleanIndex = 0;

        /// <summary>
        /// 当历史状态发生变化时触发。
        /// </summary>
        public event EventHandler<HistoryChangedEventArgs>? HistoryChanged;

        /// <summary>
        /// 获取或设置最大历史容量。
        /// </summary>
        /// <remarks>
        /// 当数量超过该值时，会自动丢弃最旧的撤销记录。
        /// </remarks>
        public int MaxCapacity
        {
            get { return _maxCapacity; }
            set
            {
                ThrowIfDisposed();

                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "MaxCapacity 必须大于 0。");

                if (_maxCapacity == value)
                    return;

                _maxCapacity = value;
                TrimCapacity();
                RaiseChanged(HistoryChangeKind.CapacityChanged);
            }
        }

        /// <summary>
        /// 获取当前是否可撤销。
        /// </summary>
        public bool CanUndo
        {
            get { return _undoStack.Count > 0; }
        }

        /// <summary>
        /// 获取当前是否可重做。
        /// </summary>
        public bool CanRedo
        {
            get { return _redoStack.Count > 0; }
        }

        /// <summary>
        /// 获取撤销栈数量。
        /// </summary>
        public int UndoCount
        {
            get { return _undoStack.Count; }
        }

        /// <summary>
        /// 获取重做栈数量。
        /// </summary>
        public int RedoCount
        {
            get { return _redoStack.Count; }
        }

        /// <summary>
        /// 获取下一次撤销的命令描述。
        /// </summary>
        public string UndoDescription
        {
            get { return CanUndo ? SafeDescription(_undoStack[_undoStack.Count - 1]) : string.Empty; }
        }

        /// <summary>
        /// 获取下一次重做的命令描述。
        /// </summary>
        public string RedoDescription
        {
            get { return CanRedo ? SafeDescription(_redoStack.Peek()) : string.Empty; }
        }

        /// <summary>
        /// 获取自上次 <see cref="MarkClean"/> 后是否有未保存的改动。
        /// </summary>
        /// <remarks>
        /// 初始值为 <c>false</c>。调用 <see cref="MarkClean"/> 可将当前状态标记为已保存；
        /// 此后每次执行、撤销或重做操作都会重新计算该值。
        /// 若存档点因历史容量裁剪而丢失，该属性将始终返回 <c>true</c>。
        /// </remarks>
        public bool IsDirty
        {
            get { return _undoStack.Count != _cleanIndex; }
        }

        /// <summary>
        /// 获取撤销栈的只读视图。索引 0 为最旧记录，末尾为下一次撤销的目标。
        /// </summary>
        public IReadOnlyList<IUndoableCommand> UndoStack
        {
            get { return _undoStack; }
        }

        /// <summary>
        /// 获取重做栈的可枚举视图。枚举顺序为栈顶（下一次重做目标）到栈底。
        /// </summary>
        public IEnumerable<IUndoableCommand> RedoStack
        {
            get { return _redoStack; }
        }

        /// <summary>
        /// 获取当前状态快照。
        /// </summary>
        public HistoryState State
        {
            get
            {
                return new HistoryState(
                    CanUndo,
                    CanRedo,
                    UndoCount,
                    RedoCount,
                    UndoDescription,
                    RedoDescription,
                    IsDirty);
            }
        }

        /// <summary>
        /// 执行一条命令，并将其加入撤销栈。
        /// </summary>
        /// <param name="command">要执行的命令。</param>
        public void Execute(IUndoableCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            ThrowIfDisposed();

            command.Redo();

            _undoStack.Add(command);
            _redoStack.Clear();
            TrimCapacity();

            RaiseChanged(HistoryChangeKind.Executed);
        }

        /// <summary>
        /// 直接压入一条已经完成的命令到撤销栈。
        /// </summary>
        /// <param name="command">要记录的命令。</param>
        /// <remarks>
        /// 适用于"外部已经先完成操作，再把撤销信息交给历史管理器记录"的场景。
        /// </remarks>
        public void Push(IUndoableCommand command)
        {
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            ThrowIfDisposed();

            _undoStack.Add(command);
            _redoStack.Clear();
            TrimCapacity();

            RaiseChanged(HistoryChangeKind.Pushed);
        }

        /// <summary>
        /// 执行一次撤销。
        /// </summary>
        /// <returns>撤销成功时返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        public bool Undo()
        {
            ThrowIfDisposed();

            if (!CanUndo)
                return false;

            IUndoableCommand command = _undoStack[_undoStack.Count - 1];
            command.Undo();                               // 先执行，异常时栈结构不受影响
            _undoStack.RemoveAt(_undoStack.Count - 1);
            _redoStack.Push(command);

            RaiseChanged(HistoryChangeKind.Undone);
            return true;
        }

        /// <summary>
        /// 执行一次重做。
        /// </summary>
        /// <returns>重做成功时返回 <c>true</c>；否则返回 <c>false</c>。</returns>
        public bool Redo()
        {
            ThrowIfDisposed();

            if (!CanRedo)
                return false;

            IUndoableCommand command = _redoStack.Peek();
            command.Redo();                               // 先执行，异常时栈结构不受影响
            _redoStack.Pop();
            _undoStack.Add(command);

            RaiseChanged(HistoryChangeKind.Redone);
            return true;
        }

        /// <summary>
        /// 将当前状态标记为已保存（存档点）。
        /// </summary>
        /// <remarks>
        /// 标记后，<see cref="IsDirty"/> 立即变为 <c>false</c>；
        /// 后续每次执行、撤销或重做都会重新计算该值，
        /// 撤销回本次标记时的状态后 <see cref="IsDirty"/> 会自动复位为 <c>false</c>。
        /// </remarks>
        public void MarkClean()
        {
            ThrowIfDisposed();

            _cleanIndex = _undoStack.Count;
            RaiseChanged(HistoryChangeKind.MarkedClean);
        }

        /// <summary>
        /// 清空撤销栈和重做栈。
        /// </summary>
        /// <remarks>
        /// 若存档点不在空栈处（即 <see cref="IsDirty"/> 为 <c>true</c>），
        /// 清空后 <see cref="IsDirty"/> 仍为 <c>true</c>。
        /// 如需将清空后的状态视为已保存，请在 <see cref="Clear"/> 后调用 <see cref="MarkClean"/>。
        /// </remarks>
        public void Clear()
        {
            ThrowIfDisposed();

            if (_undoStack.Count == 0 && _redoStack.Count == 0)
                return;

            _undoStack.Clear();
            _redoStack.Clear();

            RaiseChanged(HistoryChangeKind.Cleared);
        }

        /// <summary>
        /// 释放当前实例占用的资源。
        /// </summary>
        public void Dispose()
        {
            if (_isDisposed)
                return;

            _undoStack.Clear();
            _redoStack.Clear();
            _isDisposed = true;
            HistoryChanged = null;
        }

        private void TrimCapacity()
        {
            while (_undoStack.Count > _maxCapacity)
            {
                _undoStack.RemoveAt(0);
                _cleanIndex--; // 负值表示存档点已丢失，IsDirty 将始终为 true
            }
        }

        private void RaiseChanged(HistoryChangeKind kind)
        {
            EventHandler<HistoryChangedEventArgs> handler = HistoryChanged;
            if (handler == null)
                return;

            handler(this, new HistoryChangedEventArgs(kind, State));
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(HistoryManager));
        }

        private static string SafeDescription(IUndoableCommand command)
        {
            if (command == null)
                return string.Empty;

            return command.Description ?? string.Empty;
        }
    }
}