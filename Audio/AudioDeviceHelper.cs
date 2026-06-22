using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Collections.Generic;

namespace KkjQuicker.Audio
{
    /// <summary>
    /// 提供音频输入 / 输出设备的基础检测与枚举辅助方法。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本类面向桌面应用的启动前预检查场景，适合用于：
    /// </para>
    /// <list type="bullet">
    /// <item><description>判断当前是否存在可用的麦克风输入设备。</description></item>
    /// <item><description>判断当前是否存在可用的系统输出设备。</description></item>
    /// <item><description>获取设备名称列表，用于日志、调试或提示用户。</description></item>
    /// </list>
    /// <para>
    /// 本类的检测结果适合作为快速预检查，但不能替代真正的启动异常处理。
    /// 即使检测通过，底层设备仍可能因权限、驱动、占用或格式不支持而初始化失败。
    /// </para>
    /// </remarks>
    public static class AudioDeviceHelper
    {
        /// <summary>
        /// 获取当前系统中可见的录音输入设备数量。
        /// </summary>
        /// <returns>输入设备数量；获取失败时返回 0。</returns>
        public static int GetInputDeviceCount()
        {
            try
            {
                return WaveInEvent.DeviceCount;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取当前系统中可见的录音输入设备名称列表。
        /// </summary>
        /// <returns>设备名称列表。若获取失败，则返回空集合。</returns>
        public static IReadOnlyList<string> GetInputDeviceNames()
        {
            var result = new List<string>();

            try
            {
                var count = WaveInEvent.DeviceCount;
                for (var i = 0; i < count; i++)
                {
                    try
                    {
                        var caps = WaveInEvent.GetCapabilities(i);
                        if (!string.IsNullOrWhiteSpace(caps.ProductName))
                            result.Add(caps.ProductName);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        /// <summary>
        /// 判断当前是否存在至少一个可见的录音输入设备。
        /// </summary>
        /// <returns>
        /// 存在可见输入设备时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        public static bool HasInputDevice()
        {
            return GetInputDeviceCount() > 0;
        }

        /// <summary>
        /// 获取当前系统中处于活动状态的音频输出设备数量。
        /// </summary>
        /// <returns>输出设备数量；获取失败时返回 0。</returns>
        public static int GetOutputDeviceCount()
        {
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    return devices == null ? 0 : devices.Count;
                }
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// 获取当前系统中处于活动状态的音频输出设备名称列表。
        /// </summary>
        /// <returns>设备名称列表。若获取失败，则返回空集合。</returns>
        public static IReadOnlyList<string> GetOutputDeviceNames()
        {
            var result = new List<string>();

            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                {
                    var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                    if (devices != null)
                    {
                        foreach (var device in devices)
                        {
                            if (device != null && !string.IsNullOrWhiteSpace(device.FriendlyName))
                                result.Add(device.FriendlyName);
                        }
                    }
                }
            }
            catch
            {
            }

            return result;
        }

        /// <summary>
        /// 判断当前是否存在至少一个活动状态的音频输出设备。
        /// </summary>
        /// <returns>
        /// 存在活动输出设备时返回 <see langword="true"/>；否则返回 <see langword="false"/>。
        /// </returns>
        public static bool HasOutputDevice()
        {
            return GetOutputDeviceCount() > 0;
        }

        /// <summary>
        /// 获取当前默认音频输出设备名称。
        /// </summary>
        /// <returns>默认输出设备名称；若获取失败，则返回 <see langword="null"/>。</returns>
        public static string GetDefaultOutputDeviceName()
        {
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                using (var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
                {
                    return device == null ? null : device.FriendlyName;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}