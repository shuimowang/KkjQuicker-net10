using System;
using System.Collections.Specialized;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

using WpfClipboard = System.Windows.Clipboard;
using FormsClipboard = System.Windows.Forms.Clipboard;

namespace KkjQuicker.Utilities
{
    /// <summary>
    /// 剪贴板读写帮助类。
    /// </summary>
    public static class ClipboardHelper
    {
        private const int ClipboardRetryCount = 5;
        private const int ClipboardRetryDelayMilliseconds = 20;

        public static bool ContainsText(
            TextDataFormat format = TextDataFormat.UnicodeText)
        {
            return TryInvokeOnSta(
                delegate
                {
                    return WpfClipboard.ContainsText(format);
                },
                false);
        }

        public static bool ContainsFileDropList()
        {
            return TryInvokeOnSta(
                delegate
                {
                    return WpfClipboard.ContainsFileDropList();
                },
                false);
        }

        public static bool ContainsImage()
        {
            return TryInvokeOnSta(
                delegate
                {
                    return WpfClipboard.ContainsImage();
                },
                false);
        }

        public static bool ContainsData(string format)
        {
            if (string.IsNullOrEmpty(format))
                return false;

            return TryInvokeOnSta(
                delegate
                {
                    return WpfClipboard.ContainsData(format);
                },
                false);
        }

        public static string? TryGetClipboardText(
            TextDataFormat format = TextDataFormat.UnicodeText)
        {
            return RetryRead(
                delegate
                {
                    if (!WpfClipboard.ContainsText(format))
                        return string.Empty;

                    return WpfClipboard.GetText(format) ?? string.Empty;
                },
                string.Empty);
        }

        public static StringCollection GetFileDropList()
        {
            StringCollection? files = RetryRead(
                delegate
                {
                    if (!WpfClipboard.ContainsFileDropList())
                        return new StringCollection();

                    StringCollection result = FormsClipboard.GetFileDropList();
                    return result ?? new StringCollection();
                },
                null);

            return files ?? new StringCollection();
        }

        public static object? GetData(string format)
        {
            if (string.IsNullOrEmpty(format))
                return null;

            for (int i = 0; i < ClipboardRetryCount; i++)
            {
                bool dataPresent = false;

                try
                {
                    object? data = InvokeOnSta(
                        delegate
                        {
                            dataPresent = WpfClipboard.ContainsData(format);

                            if (!dataPresent)
                                return null;

                            return FormsClipboard.GetData(format);
                        });

                    if (!dataPresent)
                        return null;

                    if (data != null)
                        return data;
                }
                catch
                {
                }

                if (i < ClipboardRetryCount - 1)
                    Thread.Sleep(ClipboardRetryDelayMilliseconds);
            }

            return null;
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

        public static bool SetText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return false;

            return RetryWrite(
                delegate
                {
                    FormsClipboard.SetText(text);
                });
        }

        public static bool SetFileDropList(StringCollection files)
        {
            if (files == null || files.Count == 0)
                return false;

            StringCollection copy = new StringCollection();

            for (int i = 0; i < files.Count; i++)
            {
                if (!string.IsNullOrEmpty(files[i]))
                    copy.Add(files[i]);
            }

            if (copy.Count == 0)
                return false;

            return RetryWrite(
                delegate
                {
                    FormsClipboard.SetFileDropList(copy);
                });
        }

        public static bool SetImage(BitmapSource image)
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

        public static bool SetDataObject(System.Windows.IDataObject dataObject, bool copy)
        {
            if (dataObject == null)
                return false;

            return RetryWrite(
                delegate
                {
                    WpfClipboard.SetDataObject(dataObject, copy);
                });
        }

        private static T? RetryRead<T>(Func<T> action, T? failedValue)
        {
            for (int i = 0; i < ClipboardRetryCount; i++)
            {
                try
                {
                    return InvokeOnSta(action);
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
            if (action == null)
                return false;

            for (int i = 0; i < ClipboardRetryCount; i++)
            {
                try
                {
                    InvokeOnSta(
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

        private static T? TryInvokeOnSta<T>(Func<T> action, T? failedValue)
        {
            try
            {
                return InvokeOnSta(action);
            }
            catch
            {
                return failedValue;
            }
        }

        private static T? InvokeOnSta<T>(Func<T> action)
        {
            if (action == null)
                return default(T);

            Dispatcher? dispatcher = null;

            if (Application.Current != null)
                dispatcher = Application.Current.Dispatcher;

            if (dispatcher != null &&
                !dispatcher.HasShutdownStarted &&
                !dispatcher.HasShutdownFinished)
            {
                if (dispatcher.CheckAccess())
                    return action();

                return dispatcher.Invoke(action);
            }

            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
                return action();

            throw new InvalidOperationException("Clipboard operation requires a WPF Dispatcher or STA thread.");
        }
    }
}