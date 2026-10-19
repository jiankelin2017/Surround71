using System;
using System.Runtime.InteropServices;

namespace Surround71.Interop
{
    internal enum ERole
    {
        eConsole = 0,
        eMultimedia = 1,
        eCommunications = 2
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    internal class CPolicyConfigClient
    {
    }

    [Guid("f8679f50-850a-45b4-be53-0e7fe1e5a1b1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, [MarshalAs(UnmanagedType.Bool)] bool bDefault, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, [MarshalAs(UnmanagedType.Bool)] bool bDefault, out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ref long period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, out IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string deviceName, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, ref PROPERTYKEY key, out IntPtr pv);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string deviceName, [MarshalAs(UnmanagedType.Bool)] bool bFxStore, ref PROPERTYKEY key, IntPtr pv);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceName, ERole role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string deviceName, [MarshalAs(UnmanagedType.Bool)] bool visible);
    }

    /// <summary>Windows 未公开的默认音频端点设置接口（用于一键切换默认播放设备）。</summary>
    internal static class PolicyConfig
    {
        public static bool SetDefaultPlayback(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            object obj = null;
            try
            {
                obj = new CPolicyConfigClient();
                var pc = (IPolicyConfig)obj;

                int hr = pc.SetDefaultEndpoint(deviceId, ERole.eConsole);
                hr |= pc.SetDefaultEndpoint(deviceId, ERole.eMultimedia);
                hr |= pc.SetDefaultEndpoint(deviceId, ERole.eCommunications);
                return hr == 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (obj != null)
                {
                    try { Marshal.ReleaseComObject(obj); } catch { }
                }
            }
        }
    }
}
