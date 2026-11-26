using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;

namespace NOVAIsland.Infrastructure.Platform;

public sealed class WindowsSystemMonitorService : ISystemMonitorService, IDisposable
{
    private readonly MMDeviceEnumerator _deviceEnumerator = new();
    private long _prevIdle;
    private long _prevKernel;
    private long _prevUser;
    private bool _hasPrevTimes;

    public Task<SystemMetrics> CaptureMetricsAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (hasBat, batPct, charging) = ReadBattery();
            var (cpu, ramPct, ramUsed, ramTotal) = ReadCpuAndMemory();
            var (vol, muted, micMuted, activeAudioId, audioDevices) = ReadAudioState();
            var (wifiConnected, wifiSsid, wifiSignal) = ReadWifiState();
            var (brightSupported, brightness) = ReadBrightness();
            var micActive = IsCapabilityActive("microphone");
            var camActive = IsCapabilityActive("webcam");

            return new SystemMetrics(
                BatteryLevel: batPct,
                HasBattery: hasBat,
                IsCharging: charging,
                Volume: vol,
                IsMuted: muted,
                Brightness: brightness,
                BrightnessSupported: brightSupported,
                WifiConnected: wifiConnected,
                WifiNetwork: wifiSsid,
                WifiSignal: wifiSignal,
                MicActive: micActive,
                MicMuted: micMuted,
                CameraActive: camActive,
                CpuUsage: cpu,
                RamUsage: ramPct,
                RamUsedGb: ramUsed,
                RamTotalGb: ramTotal,
                ActiveAudioDeviceId: activeAudioId,
                AudioDevices: audioDevices);
        }, cancellationToken);
    }

    public Task SetMasterVolumeAsync(int volumePercent, bool muted, CancellationToken cancellationToken = default)
    {
        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volumePercent, 0, 100) / 100f;
            dev.AudioEndpointVolume.Mute = muted;
        }
        catch
        {
            // No default render device
        }
        return Task.CompletedTask;
    }

    public Task SetMicrophoneMutedAsync(bool muted, CancellationToken cancellationToken = default)
    {
        try
        {
            using var dev = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            dev.AudioEndpointVolume.Mute = muted;
        }
        catch
        {
            // No default capture device
        }
        return Task.CompletedTask;
    }

    public Task<bool> SetBrightnessAsync(int brightnessPercent, CancellationToken cancellationToken = default)
    {
        var level = (byte)Math.Clamp(brightnessPercent, 0, 100);
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods");
            foreach (ManagementObject obj in searcher.Get())
            {
                obj.InvokeMethod("WmiSetBrightness", [1, level]);
                return Task.FromResult(true);
            }
        }
        catch
        {
            // Internal WMI brightness unavailable (desktop monitor)
        }

        return Task.FromResult(SetExternalMonitorDdcBrightness(level));
    }

    public Task<bool> SetDefaultAudioDeviceAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        try
        {
            var policyConfig = (IPolicyConfig)new PolicyConfigClient();
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eConsole);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
            policyConfig.SetDefaultEndpoint(deviceId, ERole.eCommunications);
            Marshal.ReleaseComObject(policyConfig);
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private (bool HasBattery, int Pct, bool Charging) ReadBattery()
    {
        if (!Win32Interop.GetSystemPowerStatus(out var status))
        {
            return (false, 100, true);
        }

        var noBattery = (status.BatteryFlag & 128) != 0;
        var pct = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : 100;
        var charging = status.ACLineStatus == 1;
        return (!noBattery, pct, charging);
    }

    private (int Cpu, int RamPct, double RamUsedGb, double RamTotalGb) ReadCpuAndMemory()
    {
        var cpu = 0;
        if (Win32Interop.GetSystemTimes(out var idle, out var kernel, out var user))
        {
            if (_hasPrevTimes)
            {
                var idleDiff = idle - _prevIdle;
                var kernelDiff = kernel - _prevKernel;
                var userDiff = user - _prevUser;
                var sys = kernelDiff + userDiff;
                if (sys > 0)
                {
                    cpu = (int)Math.Clamp(Math.Round((sys - idleDiff) * 100.0 / sys), 0, 100);
                }
            }
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            _hasPrevTimes = true;
        }

        var mem = new Win32Interop.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Win32Interop.MEMORYSTATUSEX>() };
        if (Win32Interop.GlobalMemoryStatusEx(ref mem))
        {
            var totalGb = Math.Round(mem.ullTotalPhys / (1024.0 * 1024 * 1024), 1);
            var usedGb = Math.Round((mem.ullTotalPhys - mem.ullAvailPhys) / (1024.0 * 1024 * 1024), 1);
            return (cpu, (int)mem.dwMemoryLoad, usedGb, totalGb);
        }

        return (cpu, 0, 0, 0);
    }

    private (int Vol, bool Muted, bool MicMuted, string? ActiveId, IReadOnlyList<AudioDevice> Devices) ReadAudioState()
    {
        var vol = 50;
        var muted = false;
        var micMuted = false;
        string? activeId = null;
        var list = new List<AudioDevice>();

        try
        {
            using var defaultRender = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            activeId = defaultRender.ID;
            vol = (int)Math.Round(defaultRender.AudioEndpointVolume.MasterVolumeLevelScalar * 100f);
            muted = defaultRender.AudioEndpointVolume.Mute;
        }
        catch { }

        try
        {
            using var defaultCapture = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            micMuted = defaultCapture.AudioEndpointVolume.Mute;
        }
        catch { }

        try
        {
            foreach (var dev in _deviceEnumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                var kind = dev.FriendlyName.Contains("Headphone", StringComparison.OrdinalIgnoreCase) ||
                           dev.FriendlyName.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
                           dev.FriendlyName.Contains("Ear", StringComparison.OrdinalIgnoreCase)
                    ? AudioDeviceKind.Headphones
                    : dev.FriendlyName.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
                      dev.FriendlyName.Contains("Scarlett", StringComparison.OrdinalIgnoreCase) ||
                      dev.FriendlyName.Contains("DAC", StringComparison.OrdinalIgnoreCase)
                        ? AudioDeviceKind.Interface
                        : AudioDeviceKind.Speakers;

                using var client = dev.CreateAudioClient();
                var rate = client?.MixFormat is { } fmt
                    ? $"{fmt.SampleRate / 1000.0:0.#} kHz · {fmt.BitsPerSample}-bit"
                    : "Active";

                list.Add(new AudioDevice(dev.ID, dev.FriendlyName, kind, rate));
            }
        }
        catch { }

        return (vol, muted, micMuted, activeId, list);
    }

    private static (bool Connected, string Ssid, int Signal) ReadWifiState()
    {
        try
        {
            var profile = Windows.Networking.Connectivity.NetworkInformation.GetInternetConnectionProfile();
            if (profile?.IsWlanConnectionProfile == true && profile.WlanConnectionProfileDetails is { } wlan)
            {
                var ssid = wlan.GetConnectedSsid();
                var bars = profile.GetSignalBars() ?? 0;
                return (!string.IsNullOrWhiteSpace(ssid), string.IsNullOrWhiteSpace(ssid) ? "Connected" : ssid, bars);
            }

            if (profile is not null &&
                profile.GetNetworkConnectivityLevel() == Windows.Networking.Connectivity.NetworkConnectivityLevel.InternetAccess)
            {
                return (true, "Ethernet / Network", 4);
            }
        }
        catch { }

        return (false, "Disconnected", 0);
    }

    private static (bool Supported, int Level) ReadBrightness()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (ManagementObject obj in searcher.Get())
            {
                return (true, Convert.ToInt32(obj["CurrentBrightness"]));
            }
        }
        catch { }

        return (false, 100);
    }

    private static bool IsCapabilityActive(string capabilityName)
    {
        try
        {
            using var baseKey = Registry.CurrentUser.OpenSubKey(
                $@"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\{capabilityName}", false);
            if (baseKey is null) return false;

            foreach (var subName in baseKey.GetSubKeyNames())
            {
                if (subName == "NonPackaged")
                {
                    using var nonPkg = baseKey.OpenSubKey("NonPackaged", false);
                    if (HasZeroStopTime(nonPkg)) return true;
                }
                else
                {
                    using var sub = baseKey.OpenSubKey(subName, false);
                    if (sub?.GetValue("LastUsedTimeStop") is long stop && stop == 0)
                    {
                        return true;
                    }
                }
            }
        }
        catch { }

        return false;
    }

    private static bool HasZeroStopTime(RegistryKey? parent)
    {
        if (parent is null) return false;
        foreach (var name in parent.GetSubKeyNames())
        {
            using var k = parent.OpenSubKey(name, false);
            if (k?.GetValue("LastUsedTimeStop") is long stop && stop == 0)
            {
                return true;
            }
        }
        return false;
    }

    private static bool SetExternalMonitorDdcBrightness(uint level)
    {
        // Best-effort DDC/CI fallback for supported external monitors
        try
        {
            var primary = Win32Interop.MonitorFromPoint(new Win32Interop.POINT { X = 1, Y = 1 }, Win32Interop.MONITOR_DEFAULTTONEAREST);
            if (!GetNumberOfPhysicalMonitorsFromHMONITOR(primary, out var count) || count == 0)
            {
                return false;
            }

            var monitors = new PHYSICAL_MONITOR[count];
            if (!GetPhysicalMonitorsFromHMONITOR(primary, count, monitors))
            {
                return false;
            }

            try
            {
                return SetMonitorBrightness(monitors[0].hPhysicalMonitor, level);
            }
            finally
            {
                DestroyPhysicalMonitors(count, monitors);
            }
        }
        catch
        {
            return false;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    private static extern bool SetMonitorBrightness(IntPtr hMonitor, uint dwNewBrightness);

    private enum ERole { eConsole = 0, eMultimedia = 1, eCommunications = 2 }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    private class PolicyConfigClient { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string pszDeviceName, IntPtr ppFormat);
        [PreserveSig] int GetDeviceFormat(string pszDeviceName, bool bDefault, IntPtr ppFormat);
        [PreserveSig] int ResetDeviceFormat(string pszDeviceName);
        [PreserveSig] int SetDeviceFormat(string pszDeviceName, IntPtr pEndpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(string pszDeviceName, bool bDefault, IntPtr pmftDefaultPeriod, IntPtr pmftMinimumPeriod);
        [PreserveSig] int SetProcessingPeriod(string pszDeviceName, IntPtr pmftPeriod);
        [PreserveSig] int GetShareMode(string pszDeviceName, IntPtr pMode);
        [PreserveSig] int SetShareMode(string pszDeviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetPropertyValue(string pszDeviceName, bool bFxStore, IntPtr key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string pszDeviceName, ERole role);
        [PreserveSig] int SetEndpointVisibility(string pszDeviceName, bool bVisible);
    }

    public void Dispose() => _deviceEnumerator.Dispose();
}
