using System;
using System.Runtime.InteropServices;

namespace EchoMix.AudioHost.Audio;

/// Sets which output device a single process plays to - the same thing Settings > System > Sound > Volume
/// mixer > (app) > Output device does, done programmatically.
public static class AudioEndpointRouter
{
    private const string ActivatableClassId = "Windows.Media.Internal.AudioPolicyConfig";

    private static readonly Guid IidVariant21H2 = new("ab3d4648-e242-459f-b02f-541c70306324");
    private static readonly Guid IidVariantDownlevel = new("2a59116d-6c4f-45e0-a74f-707e3fef9258");

    private const string MmDevApiToken = @"\\?\SWD#MMDEVAPI#";
    private const string DevInterfaceAudioRender = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";

    private static IAudioPolicyConfig? cached;
    private static bool activationAttempted;

    /// Null when this Windows build doesn't expose the interface at all, which is the case the whole design
    /// has to survive rather than assume away.
    public static IAudioPolicyConfig? TryGet()
    {
        if (activationAttempted)
            return cached;

        activationAttempted = true;

        var useModern = Environment.OSVersion.Version.Build >= 22000;
        var iid = useModern ? IidVariant21H2 : IidVariantDownlevel;

        var classId = IntPtr.Zero;
        try
        {
            var hr = WindowsCreateString(ActivatableClassId, (uint)ActivatableClassId.Length, out classId);
            if (hr != 0)
                throw new COMException("WindowsCreateString failed.", hr);

            hr = RoGetActivationFactory(classId, ref iid, out var factoryPtr);
            if (hr != 0 || factoryPtr == IntPtr.Zero)
                throw new COMException("RoGetActivationFactory failed.", hr);

            try
            {
                cached = (IAudioPolicyConfig)Marshal.GetTypedObjectForIUnknown(factoryPtr, typeof(IAudioPolicyConfig));
            }
            finally
            {
                Marshal.Release(factoryPtr);
            }

            Log.Info($"Per-app audio routing available (using the {(useModern ? "21H2+" : "downlevel")} interface).");
        }
        catch (Exception ex)
        {
            Log.Warn($"Per-app audio routing is unavailable on this Windows build ({Environment.OSVersion.Version}): {ex.Message}");
            cached = null;
        }
        finally
        {
            if (classId != IntPtr.Zero)
            {
                try { WindowsDeleteString(classId); } catch { }
            }
        }

        return cached;
    }

    public static bool IsAvailable => TryGet() != null;

    /// Routes one process's audio to deviceId (a raw MMDevice id from AudioEndpoints).
    public static bool SetProcessEndpoint(int processId, string? deviceId)
    {
        var config = TryGet();
        if (config == null)
            return false;

        var hstring = IntPtr.Zero;
        try
        {
            if (!string.IsNullOrWhiteSpace(deviceId))
            {
                var path = WrapDeviceId(deviceId);
                WindowsCreateString(path, (uint)path.Length, out hstring);
            }

            var multimedia = config.SetPersistedDefaultAudioEndpoint((uint)processId, DataFlowRender, RoleMultimedia, hstring);
            var console = config.SetPersistedDefaultAudioEndpoint((uint)processId, DataFlowRender, RoleConsole, hstring);

            if (multimedia != 0 || console != 0)
            {
                Log.Warn($"Routing process {processId} failed (multimedia 0x{multimedia:X8}, console 0x{console:X8}).");
                return false;
            }

            Log.Info(string.IsNullOrWhiteSpace(deviceId)
                ? $"Restored process {processId} to the default output device."
                : $"Routed process {processId} to device {deviceId}.");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Routing process {processId} threw", ex);
            return false;
        }
        finally
        {
            if (hstring != IntPtr.Zero)
            {
                try { WindowsDeleteString(hstring); } catch { }
            }
        }
    }

    /// The device this process is currently pinned to, or null if it follows the system default.
    public static string? GetProcessEndpoint(int processId)
    {
        var config = TryGet();
        if (config == null)
            return null;

        var hstring = IntPtr.Zero;
        try
        {
            var hr = config.GetPersistedDefaultAudioEndpoint((uint)processId, DataFlowRender, RoleMultimedia, out hstring);
            if (hr != 0 || hstring == IntPtr.Zero)
                return null;

            var buffer = WindowsGetStringRawBuffer(hstring, out var length);
            if (buffer == IntPtr.Zero || length == 0)
                return null;

            var deviceId = Marshal.PtrToStringUni(buffer, (int)length);
            return string.IsNullOrWhiteSpace(deviceId) ? null : UnwrapDeviceId(deviceId);
        }
        catch (Exception ex)
        {
            Log.Error($"Reading the endpoint for process {processId} threw", ex);
            return null;
        }
        finally
        {
            if (hstring != IntPtr.Zero)
            {
                try { WindowsDeleteString(hstring); } catch { }
            }
        }
    }

    private static string WrapDeviceId(string deviceId) =>
        $"{MmDevApiToken}{deviceId}{DevInterfaceAudioRender}";

    private static string UnwrapDeviceId(string deviceId)
    {
        if (deviceId.StartsWith(MmDevApiToken, StringComparison.Ordinal))
            deviceId = deviceId[MmDevApiToken.Length..];
        if (deviceId.EndsWith(DevInterfaceAudioRender, StringComparison.Ordinal))
            deviceId = deviceId[..^DevInterfaceAudioRender.Length];
        return deviceId;
    }


    private const int DataFlowRender = 0;
    private const int RoleConsole = 0;
    private const int RoleMultimedia = 1;

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll")]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string src,
        uint length,
        out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);

    /// Declared as IUnknown rather than IInspectable because .NET no longer supports
    /// ComInterfaceType.InterfaceIsIInspectable - so IInspectable's own three methods are spelled out here
    /// instead, ahead of everything else, to keep the vtable slots aligned.
    [ComImport]
    [Guid("ab3d4648-e242-459f-b02f-541c70306324")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioPolicyConfig
    {
        [PreserveSig] int GetIids(out int iidCount, out IntPtr iids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr className);
        [PreserveSig] int GetTrustLevel(out int trustLevel);

        int __incomplete__add_CtxVolumeChange();
        int __incomplete__remove_CtxVolumeChanged();
        int __incomplete__add_RingerVibrateStateChanged();
        int __incomplete__remove_RingerVibrateStateChange();
        int __incomplete__SetVolumeGroupGainForId();
        int __incomplete__GetVolumeGroupGainForId();
        int __incomplete__GetActiveVolumeGroupForEndpointId();
        int __incomplete__GetVolumeGroupsForEndpoint();
        int __incomplete__GetCurrentVolumeContext();
        int __incomplete__SetVolumeGroupMuteForId();
        int __incomplete__GetVolumeGroupMuteForId();
        int __incomplete__SetRingerVibrateState();
        int __incomplete__GetRingerVibrateState();
        int __incomplete__SetPreferredChatApplication();
        int __incomplete__ResetPreferredChatApplication();
        int __incomplete__GetPreferredChatApplication();
        int __incomplete__GetCurrentChatApplications();
        int __incomplete__add_ChatContextChanged();
        int __incomplete__remove_ChatContextChanged();

        [PreserveSig]
        int SetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, IntPtr deviceId);

        /// Returns an HSTRING.
        [PreserveSig]
        int GetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, out IntPtr deviceId);

        [PreserveSig]
        int ClearAllPersistedApplicationDefaultEndpoints();
    }
}
