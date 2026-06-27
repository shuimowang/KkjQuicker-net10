using System;
using System.Collections.Specialized;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using IDataObject = System.Windows.IDataObject;
using WpfClipboard = System.Windows.Clipboard;

namespace KkjQuicker.Utilities
{
    /// <summary>
    /// 提供带 STA 调度和短暂重试的剪贴板读写方法。
    /// </summary>
    public static class ClipboardHelper
    {
        private const int ClipboardRetryCount = 8;
        private const int ClipboardRetryDelayMilliseconds = 50;

        public static bool ContainsText(TextDataFormat format = TextDataFormat.UnicodeText)
        {
            return TryInvokeOnClipboardThread(
                delegate
                {
                    return WpfClipboard.ContainsText(format);
                },
                false);
        }

        public static bool ContainsFileDropList()
        {
            return TryInvokeOnClipboardThread(
                delegate
                {
                    return WpfClipboard.ContainsFileDropList();
                },
                false);
        }

        public static bool ContainsImage()
        {
            return TryInvokeOnClipboardThread(
                delegate
                {
                    return WpfClipboard.ContainsImage();
                },
                false);
        }

        public static bool ContainsData(string? format)
        {
            if (string.IsNullOrEmpty(format))
                return false;

            return TryInvokeOnClipboardThread(
                delegate
                {
                    return WpfClipboard.ContainsData(format);
                },
                false);
        }

        public static string TryGetClipboardText(TextDataFormat format = TextDataFormat.UnicodeText)
        {
            return RetryRead(
                delegate
                {
                    return WpfClipboard.ContainsText(format)
                        ? WpfClipboard.GetText(format) ?? string.Empty
                        : string.Empty;
                },
                string.Empty);
        }

        public static StringCollection GetFileDropList()
        {
            StringCollection? files = RetryRead(
                delegate
                {
                    return WpfClipboard.ContainsFileDropList()
                        ? WpfClipboard.GetFileDropList()
                        : new StringCollection();
                },
                null);

            return files ?? new StringCollection();
        }

        public static object? GetData(string? format)
        {
            if (string.IsNullOrEmpty(format))
                return null;

            return RetryRead(
                delegate
                {
                    return WpfClipboard.ContainsData(format)
                        ? WpfClipboard.GetData(format)
                        : null;
                },
                null);
        }

        public static BitmapSource? GetImage()
        {
            return RetryRead(
                delegate
                {
                    if (!WpfClipboard.ContainsImage())
                        return null;

                    BitmapSource? image = WpfClipboard.GetImage();
                    if (image != null && image.CanFreeze)
                        image.Freeze();

                    return image;
                },
                null);
        }

        public static bool SetText(string? text)
        {
            if (text == null)
                return false;

            return RetryWrite(
                delegate
                {
                    WpfClipboard.SetText(text);
                });
        }

        public static bool SetFileDropList(StringCollection? files)
        {
            if (files == null || files.Count == 0)
                return false;

            var copy = new StringCollection();
            for (int i = 0; i < files.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(files[i]))
                    copy.Add(files[i]);
            }

            if (copy.Count == 0)
                return false;

            return RetryWrite(
                delegate
                {
                    WpfClipboard.SetFileDropList(copy);
                });
        }

        public static bool SetImage(BitmapSource? image)
        {
            if (image == null)
                return false;

            try
            {
                if (!image.IsFrozen && image.CanFreeze)
                    image.Freeze();
            }
            catch
            {
                return false;
            }

            return RetryWrite(
                delegate
                {
                    WpfClipboard.SetImage(image);
                });
        }

        public static bool SetDataObject(IDataObject? dataObject, bool copy)
        {
            if (dataObject == null)
                return false;

            return RetryWrite(
                delegate
                {
                    WpfClipboard.SetDataObject(dataObject, copy);
                });
        }

        private static T RetryRead<T>(Func<T> action, T failedValue)
        {
            for (int i = 0; i < ClipboardRetryCount; i++)
            {
                try
                {
                    return InvokeOnClipboardThread(action);
                }
                catch
                {
                }

                if (i < ClipboardRetryCount - 1)
                    Thread.Sleep(ClipboardRetryDelayMilliseconds);
            }

            return failedValue;
        }

        private static bool RetryWrite(Action action)
        {
            for (int i = 0; i < ClipboardRetryCount; i++)
            {
                try
                {
                    InvokeOnClipboardThread(
                        delegate
                        {
                            action();
                            return true;
                        });

                    return true;
                }
                catch
                {
                }

                if (i < ClipboardRetryCount - 1)
                    Thread.Sleep(ClipboardRetryDelayMilliseconds);
            }

            return false;
        }

        private static T TryInvokeOnClipboardThread<T>(Func<T> action, T failedValue)
        {
            try
            {
                return InvokeOnClipboardThread(action);
            }
            catch
            {
                return failedValue;
            }
        }

        private static T InvokeOnClipboardThread<T>(Func<T> action)
        {
            ArgumentNullException.ThrowIfNull(action);

            Dispatcher? dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null &&
                !dispatcher.HasShutdownStarted &&
                !dispatcher.HasShutdownFinished)
            {
                return dispatcher.CheckAccess()
                    ? action()
                    : dispatcher.Invoke(action);
            }

            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return action();

            return InvokeOnTemporaryStaThread(action);
        }

        private static T InvokeOnTemporaryStaThread<T>(Func<T> action)
        {
            T? result = default;
            ExceptionDispatchInfo? exception = null;

            var thread = new Thread(new ThreadStart(
                delegate
                {
                    try
                    {
                        result = action();
                    }
                    catch (Exception ex)
                    {
                        exception = ExceptionDispatchInfo.Capture(ex);
                    }
                }));

            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
            thread.Join();

            exception?.Throw();
            return result!;
        }
    }
}
