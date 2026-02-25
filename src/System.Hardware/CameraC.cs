// machine generated, do not edit
using System;
using System.Runtime.InteropServices;
using M = System.Runtime.InteropServices.MarshalAsAttribute;
using U = System.Runtime.InteropServices.UnmanagedType;

namespace System.Hardware
{
public static unsafe partial class CameraC
{
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate void camFrameCallback(IntPtr device, camFrame* frame, void* userdata);
[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate void camPermissionCallback(IntPtr device, camPermission result, void* userdata);

public enum camPixelFormat
{
    CAM_PIXEL_FORMAT_UNKNOWN = 0,
    CAM_PIXEL_FORMAT_RGB24,
    CAM_PIXEL_FORMAT_BGR24,
    CAM_PIXEL_FORMAT_RGBA32,
    CAM_PIXEL_FORMAT_BGRA32,
    CAM_PIXEL_FORMAT_ARGB32,
    CAM_PIXEL_FORMAT_XRGB8888,
    CAM_PIXEL_FORMAT_RGB565,
    CAM_PIXEL_FORMAT_XRGB1555,
    CAM_PIXEL_FORMAT_NV12,
    CAM_PIXEL_FORMAT_NV21,
    CAM_PIXEL_FORMAT_YUY2,
    CAM_PIXEL_FORMAT_UYVY,
    CAM_PIXEL_FORMAT_YVYU,
    CAM_PIXEL_FORMAT_YV12,
    CAM_PIXEL_FORMAT_IYUV,
    CAM_PIXEL_FORMAT_P010,
    CAM_PIXEL_FORMAT_MJPEG,
    CAM_PIXEL_FORMAT_COUNT,
}
public enum camPosition
{
    CAM_POSITION_UNKNOWN = 0,
    CAM_POSITION_FRONT_FACING,
    CAM_POSITION_BACK_FACING,
}
public enum camPermission
{
    CAM_PERMISSION_UNKNOWN = 0,
    CAM_PERMISSION_PENDING,
    CAM_PERMISSION_APPROVED,
    CAM_PERMISSION_DENIED,
}
[StructLayout(LayoutKind.Sequential)]
public struct camSpec
{
    public int width;
    public int height;
    public int fps_numerator;
    public int fps_denominator;
    public camPixelFormat format;
}
[StructLayout(LayoutKind.Sequential)]
public struct camDeviceInfo
{
    #pragma warning disable 169
    public struct nameCollection
    {
        public ref byte this[int index] => ref MemoryMarshal.CreateSpan(ref _item0, 256)[index];
        private byte _item0;
        private byte _item1;
        private byte _item2;
        private byte _item3;
        private byte _item4;
        private byte _item5;
        private byte _item6;
        private byte _item7;
        private byte _item8;
        private byte _item9;
        private byte _item10;
        private byte _item11;
        private byte _item12;
        private byte _item13;
        private byte _item14;
        private byte _item15;
        private byte _item16;
        private byte _item17;
        private byte _item18;
        private byte _item19;
        private byte _item20;
        private byte _item21;
        private byte _item22;
        private byte _item23;
        private byte _item24;
        private byte _item25;
        private byte _item26;
        private byte _item27;
        private byte _item28;
        private byte _item29;
        private byte _item30;
        private byte _item31;
        private byte _item32;
        private byte _item33;
        private byte _item34;
        private byte _item35;
        private byte _item36;
        private byte _item37;
        private byte _item38;
        private byte _item39;
        private byte _item40;
        private byte _item41;
        private byte _item42;
        private byte _item43;
        private byte _item44;
        private byte _item45;
        private byte _item46;
        private byte _item47;
        private byte _item48;
        private byte _item49;
        private byte _item50;
        private byte _item51;
        private byte _item52;
        private byte _item53;
        private byte _item54;
        private byte _item55;
        private byte _item56;
        private byte _item57;
        private byte _item58;
        private byte _item59;
        private byte _item60;
        private byte _item61;
        private byte _item62;
        private byte _item63;
        private byte _item64;
        private byte _item65;
        private byte _item66;
        private byte _item67;
        private byte _item68;
        private byte _item69;
        private byte _item70;
        private byte _item71;
        private byte _item72;
        private byte _item73;
        private byte _item74;
        private byte _item75;
        private byte _item76;
        private byte _item77;
        private byte _item78;
        private byte _item79;
        private byte _item80;
        private byte _item81;
        private byte _item82;
        private byte _item83;
        private byte _item84;
        private byte _item85;
        private byte _item86;
        private byte _item87;
        private byte _item88;
        private byte _item89;
        private byte _item90;
        private byte _item91;
        private byte _item92;
        private byte _item93;
        private byte _item94;
        private byte _item95;
        private byte _item96;
        private byte _item97;
        private byte _item98;
        private byte _item99;
        private byte _item100;
        private byte _item101;
        private byte _item102;
        private byte _item103;
        private byte _item104;
        private byte _item105;
        private byte _item106;
        private byte _item107;
        private byte _item108;
        private byte _item109;
        private byte _item110;
        private byte _item111;
        private byte _item112;
        private byte _item113;
        private byte _item114;
        private byte _item115;
        private byte _item116;
        private byte _item117;
        private byte _item118;
        private byte _item119;
        private byte _item120;
        private byte _item121;
        private byte _item122;
        private byte _item123;
        private byte _item124;
        private byte _item125;
        private byte _item126;
        private byte _item127;
        private byte _item128;
        private byte _item129;
        private byte _item130;
        private byte _item131;
        private byte _item132;
        private byte _item133;
        private byte _item134;
        private byte _item135;
        private byte _item136;
        private byte _item137;
        private byte _item138;
        private byte _item139;
        private byte _item140;
        private byte _item141;
        private byte _item142;
        private byte _item143;
        private byte _item144;
        private byte _item145;
        private byte _item146;
        private byte _item147;
        private byte _item148;
        private byte _item149;
        private byte _item150;
        private byte _item151;
        private byte _item152;
        private byte _item153;
        private byte _item154;
        private byte _item155;
        private byte _item156;
        private byte _item157;
        private byte _item158;
        private byte _item159;
        private byte _item160;
        private byte _item161;
        private byte _item162;
        private byte _item163;
        private byte _item164;
        private byte _item165;
        private byte _item166;
        private byte _item167;
        private byte _item168;
        private byte _item169;
        private byte _item170;
        private byte _item171;
        private byte _item172;
        private byte _item173;
        private byte _item174;
        private byte _item175;
        private byte _item176;
        private byte _item177;
        private byte _item178;
        private byte _item179;
        private byte _item180;
        private byte _item181;
        private byte _item182;
        private byte _item183;
        private byte _item184;
        private byte _item185;
        private byte _item186;
        private byte _item187;
        private byte _item188;
        private byte _item189;
        private byte _item190;
        private byte _item191;
        private byte _item192;
        private byte _item193;
        private byte _item194;
        private byte _item195;
        private byte _item196;
        private byte _item197;
        private byte _item198;
        private byte _item199;
        private byte _item200;
        private byte _item201;
        private byte _item202;
        private byte _item203;
        private byte _item204;
        private byte _item205;
        private byte _item206;
        private byte _item207;
        private byte _item208;
        private byte _item209;
        private byte _item210;
        private byte _item211;
        private byte _item212;
        private byte _item213;
        private byte _item214;
        private byte _item215;
        private byte _item216;
        private byte _item217;
        private byte _item218;
        private byte _item219;
        private byte _item220;
        private byte _item221;
        private byte _item222;
        private byte _item223;
        private byte _item224;
        private byte _item225;
        private byte _item226;
        private byte _item227;
        private byte _item228;
        private byte _item229;
        private byte _item230;
        private byte _item231;
        private byte _item232;
        private byte _item233;
        private byte _item234;
        private byte _item235;
        private byte _item236;
        private byte _item237;
        private byte _item238;
        private byte _item239;
        private byte _item240;
        private byte _item241;
        private byte _item242;
        private byte _item243;
        private byte _item244;
        private byte _item245;
        private byte _item246;
        private byte _item247;
        private byte _item248;
        private byte _item249;
        private byte _item250;
        private byte _item251;
        private byte _item252;
        private byte _item253;
        private byte _item254;
        private byte _item255;
    }
    #pragma warning restore 169
    public nameCollection name;
    #pragma warning disable 169
    public struct device_idCollection
    {
        public ref byte this[int index] => ref MemoryMarshal.CreateSpan(ref _item0, 256)[index];
        private byte _item0;
        private byte _item1;
        private byte _item2;
        private byte _item3;
        private byte _item4;
        private byte _item5;
        private byte _item6;
        private byte _item7;
        private byte _item8;
        private byte _item9;
        private byte _item10;
        private byte _item11;
        private byte _item12;
        private byte _item13;
        private byte _item14;
        private byte _item15;
        private byte _item16;
        private byte _item17;
        private byte _item18;
        private byte _item19;
        private byte _item20;
        private byte _item21;
        private byte _item22;
        private byte _item23;
        private byte _item24;
        private byte _item25;
        private byte _item26;
        private byte _item27;
        private byte _item28;
        private byte _item29;
        private byte _item30;
        private byte _item31;
        private byte _item32;
        private byte _item33;
        private byte _item34;
        private byte _item35;
        private byte _item36;
        private byte _item37;
        private byte _item38;
        private byte _item39;
        private byte _item40;
        private byte _item41;
        private byte _item42;
        private byte _item43;
        private byte _item44;
        private byte _item45;
        private byte _item46;
        private byte _item47;
        private byte _item48;
        private byte _item49;
        private byte _item50;
        private byte _item51;
        private byte _item52;
        private byte _item53;
        private byte _item54;
        private byte _item55;
        private byte _item56;
        private byte _item57;
        private byte _item58;
        private byte _item59;
        private byte _item60;
        private byte _item61;
        private byte _item62;
        private byte _item63;
        private byte _item64;
        private byte _item65;
        private byte _item66;
        private byte _item67;
        private byte _item68;
        private byte _item69;
        private byte _item70;
        private byte _item71;
        private byte _item72;
        private byte _item73;
        private byte _item74;
        private byte _item75;
        private byte _item76;
        private byte _item77;
        private byte _item78;
        private byte _item79;
        private byte _item80;
        private byte _item81;
        private byte _item82;
        private byte _item83;
        private byte _item84;
        private byte _item85;
        private byte _item86;
        private byte _item87;
        private byte _item88;
        private byte _item89;
        private byte _item90;
        private byte _item91;
        private byte _item92;
        private byte _item93;
        private byte _item94;
        private byte _item95;
        private byte _item96;
        private byte _item97;
        private byte _item98;
        private byte _item99;
        private byte _item100;
        private byte _item101;
        private byte _item102;
        private byte _item103;
        private byte _item104;
        private byte _item105;
        private byte _item106;
        private byte _item107;
        private byte _item108;
        private byte _item109;
        private byte _item110;
        private byte _item111;
        private byte _item112;
        private byte _item113;
        private byte _item114;
        private byte _item115;
        private byte _item116;
        private byte _item117;
        private byte _item118;
        private byte _item119;
        private byte _item120;
        private byte _item121;
        private byte _item122;
        private byte _item123;
        private byte _item124;
        private byte _item125;
        private byte _item126;
        private byte _item127;
        private byte _item128;
        private byte _item129;
        private byte _item130;
        private byte _item131;
        private byte _item132;
        private byte _item133;
        private byte _item134;
        private byte _item135;
        private byte _item136;
        private byte _item137;
        private byte _item138;
        private byte _item139;
        private byte _item140;
        private byte _item141;
        private byte _item142;
        private byte _item143;
        private byte _item144;
        private byte _item145;
        private byte _item146;
        private byte _item147;
        private byte _item148;
        private byte _item149;
        private byte _item150;
        private byte _item151;
        private byte _item152;
        private byte _item153;
        private byte _item154;
        private byte _item155;
        private byte _item156;
        private byte _item157;
        private byte _item158;
        private byte _item159;
        private byte _item160;
        private byte _item161;
        private byte _item162;
        private byte _item163;
        private byte _item164;
        private byte _item165;
        private byte _item166;
        private byte _item167;
        private byte _item168;
        private byte _item169;
        private byte _item170;
        private byte _item171;
        private byte _item172;
        private byte _item173;
        private byte _item174;
        private byte _item175;
        private byte _item176;
        private byte _item177;
        private byte _item178;
        private byte _item179;
        private byte _item180;
        private byte _item181;
        private byte _item182;
        private byte _item183;
        private byte _item184;
        private byte _item185;
        private byte _item186;
        private byte _item187;
        private byte _item188;
        private byte _item189;
        private byte _item190;
        private byte _item191;
        private byte _item192;
        private byte _item193;
        private byte _item194;
        private byte _item195;
        private byte _item196;
        private byte _item197;
        private byte _item198;
        private byte _item199;
        private byte _item200;
        private byte _item201;
        private byte _item202;
        private byte _item203;
        private byte _item204;
        private byte _item205;
        private byte _item206;
        private byte _item207;
        private byte _item208;
        private byte _item209;
        private byte _item210;
        private byte _item211;
        private byte _item212;
        private byte _item213;
        private byte _item214;
        private byte _item215;
        private byte _item216;
        private byte _item217;
        private byte _item218;
        private byte _item219;
        private byte _item220;
        private byte _item221;
        private byte _item222;
        private byte _item223;
        private byte _item224;
        private byte _item225;
        private byte _item226;
        private byte _item227;
        private byte _item228;
        private byte _item229;
        private byte _item230;
        private byte _item231;
        private byte _item232;
        private byte _item233;
        private byte _item234;
        private byte _item235;
        private byte _item236;
        private byte _item237;
        private byte _item238;
        private byte _item239;
        private byte _item240;
        private byte _item241;
        private byte _item242;
        private byte _item243;
        private byte _item244;
        private byte _item245;
        private byte _item246;
        private byte _item247;
        private byte _item248;
        private byte _item249;
        private byte _item250;
        private byte _item251;
        private byte _item252;
        private byte _item253;
        private byte _item254;
        private byte _item255;
    }
    #pragma warning restore 169
    public device_idCollection device_id;
    public camPosition position;
    public camSpec* specs;
    public int num_specs;
}
[StructLayout(LayoutKind.Sequential)]
public struct camFrame
{
    public void* data;
    public int pitch;
    public void* data2;
    public int pitch2;
    public int width;
    public int height;
    public camPixelFormat format;
    public ulong timestamp_ns;
    public float rotation;
}
#if WEB
[DllImport("camerac", EntryPoint = "cam_init", CallingConvention = CallingConvention.Cdecl)]
private static extern int cam_init_native();
public static bool cam_init() => cam_init_native() != 0;
#else
#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_init", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_init", CallingConvention = CallingConvention.Cdecl)]
#endif
[return: M(U.I1)]
public static extern bool cam_init();
#endif

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_shutdown", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_shutdown", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern void cam_shutdown();

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_get_backend", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_get_backend", CallingConvention = CallingConvention.Cdecl)]
#endif
private static extern IntPtr cam_get_backend_native();

public static string cam_get_backend()
{
    IntPtr ptr = cam_get_backend_native();
    if (ptr == IntPtr.Zero)
        return "";

    // Manual UTF-8 to string conversion to avoid marshalling corruption
    try
    {
        return Marshal.PtrToStringUTF8(ptr) ?? "";
    }
    catch
    {
        // Fallback in case of any marshalling issues
        return "";
    }
}

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_get_device_count", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_get_device_count", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern int cam_get_device_count();

#if WEB
[DllImport("camerac", EntryPoint = "cam_get_device_info", CallingConvention = CallingConvention.Cdecl)]
private static extern int cam_get_device_info_native(int index, camDeviceInfo* out_info);
public static bool cam_get_device_info(int index, camDeviceInfo* out_info) => cam_get_device_info_native(index, out_info) != 0;
#else
#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_get_device_info", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_get_device_info", CallingConvention = CallingConvention.Cdecl)]
#endif
[return: M(U.I1)]
public static extern bool cam_get_device_info(int index, camDeviceInfo* out_info);
#endif

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_free_device_info", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_free_device_info", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern void cam_free_device_info(camDeviceInfo* info);

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_open", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_open", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern IntPtr cam_open(int device_index, in camSpec requested, camFrameCallback frame_cb, void* userdata);

// Overload accepting a raw function pointer (IntPtr) – required for [UnmanagedCallersOnly] callbacks
// used by NativeAOT / WebAssembly targets where managed delegate thunks are not supported.
#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_open", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_open", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern IntPtr cam_open(int device_index, in camSpec requested, IntPtr frame_cb, void* userdata);

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_close", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_close", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern void cam_close(IntPtr device);

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_get_permission", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_get_permission", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern camPermission cam_get_permission(IntPtr device);

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_set_permission_callback", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_set_permission_callback", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern void cam_set_permission_callback(IntPtr device, camPermissionCallback cb, void* userdata);

// Overload accepting a raw function pointer – required for [UnmanagedCallersOnly] callbacks.
#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_set_permission_callback", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_set_permission_callback", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern void cam_set_permission_callback(IntPtr device, IntPtr cb, void* userdata);

#if WEB
[DllImport("camerac", EntryPoint = "cam_get_actual_spec", CallingConvention = CallingConvention.Cdecl)]
private static extern int cam_get_actual_spec_native(IntPtr device, camSpec* out_spec);
public static bool cam_get_actual_spec(IntPtr device, camSpec* out_spec) => cam_get_actual_spec_native(device, out_spec) != 0;
#else
#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_get_actual_spec", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_get_actual_spec", CallingConvention = CallingConvention.Cdecl)]
#endif
[return: M(U.I1)]
public static extern bool cam_get_actual_spec(IntPtr device, camSpec* out_spec);
#endif

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_get_device_name", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_get_device_name", CallingConvention = CallingConvention.Cdecl)]
#endif
private static extern IntPtr cam_get_device_name_native(IntPtr device);

public static string cam_get_device_name(IntPtr device)
{
    IntPtr ptr = cam_get_device_name_native(device);
    if (ptr == IntPtr.Zero)
        return "";

    // Manual UTF-8 to string conversion to avoid marshalling corruption
    try
    {
        return Marshal.PtrToStringUTF8(ptr) ?? "";
    }
    catch
    {
        // Fallback in case of any marshalling issues
        return "";
    }
}

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_get_device_position", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_get_device_position", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern camPosition cam_get_device_position(IntPtr device);

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_update", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_update", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern void cam_update();

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_bytes_per_pixel", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_bytes_per_pixel", CallingConvention = CallingConvention.Cdecl)]
#endif
public static extern int cam_bytes_per_pixel(camPixelFormat format);

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_pixel_format_name", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_pixel_format_name", CallingConvention = CallingConvention.Cdecl)]
#endif
private static extern IntPtr cam_pixel_format_name_native(camPixelFormat format);

public static string cam_pixel_format_name(camPixelFormat format)
{
    IntPtr ptr = cam_pixel_format_name_native(format);
    if (ptr == IntPtr.Zero)
        return "";

    // Manual UTF-8 to string conversion to avoid marshalling corruption
    try
    {
        return Marshal.PtrToStringUTF8(ptr) ?? "";
    }
    catch
    {
        // Fallback in case of any marshalling issues
        return "";
    }
}

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_position_name", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_position_name", CallingConvention = CallingConvention.Cdecl)]
#endif
private static extern IntPtr cam_position_name_native(camPosition position);

public static string cam_position_name(camPosition position)
{
    IntPtr ptr = cam_position_name_native(position);
    if (ptr == IntPtr.Zero)
        return "";

    // Manual UTF-8 to string conversion to avoid marshalling corruption
    try
    {
        return Marshal.PtrToStringUTF8(ptr) ?? "";
    }
    catch
    {
        // Fallback in case of any marshalling issues
        return "";
    }
}

#if __IOS__
[DllImport("@rpath/camerac.framework/camerac", EntryPoint = "cam_get_error", CallingConvention = CallingConvention.Cdecl)]
#else
[DllImport("camerac", EntryPoint = "cam_get_error", CallingConvention = CallingConvention.Cdecl)]
#endif
private static extern IntPtr cam_get_error_native();

public static string cam_get_error()
{
    IntPtr ptr = cam_get_error_native();
    if (ptr == IntPtr.Zero)
        return "";

    // Manual UTF-8 to string conversion to avoid marshalling corruption
    try
    {
        return Marshal.PtrToStringUTF8(ptr) ?? "";
    }
    catch
    {
        // Fallback in case of any marshalling issues
        return "";
    }
}

}
}
