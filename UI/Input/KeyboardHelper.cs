using System;
using System.Windows.Input;

namespace KkjQuicker.UI.Input
{
    public static class KeyboardHelper
    {
        /// <summary>
        /// 从 <see cref="KeyEventArgs"/> 中获取真实按键，自动解包
        /// <see cref="Key.System"/> / <see cref="Key.ImeProcessed"/> / <see cref="Key.DeadCharProcessed"/>。
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="e"/> 为 null。</exception>
        public static Key GetRealKey(KeyEventArgs e)
        {
            ArgumentNullException.ThrowIfNull(e);
            if (e.Key == Key.System) return e.SystemKey;
            if (e.Key == Key.ImeProcessed) return e.ImeProcessedKey;
            if (e.Key == Key.DeadCharProcessed) return e.DeadCharProcessedKey;
            return e.Key;
        }
    }
}
