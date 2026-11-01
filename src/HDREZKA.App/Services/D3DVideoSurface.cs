using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Controls;

namespace HDREZKA.App.Services;

/// <summary>Direct GPU rendering into the existing XAML tree. No child HWND,
/// screen capture or CPU readback: transport controls stay above the video.</summary>
internal sealed unsafe class D3DVideoSurface : IDisposable
{
    private IntPtr _factory, _device, _context, _swapChain, _swapChain2, _panel;
    private int _width, _height;
    private float _scaleX = float.NaN, _scaleY = float.NaN;
    public IntPtr Device => _device;
    public string AdapterName { get; private set; } = "";
    private static void** V(IntPtr p) => *(void***)p;
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    private static void Release(ref IntPtr p) { if (p == IntPtr.Zero) return; ((delegate* unmanaged[Stdcall]<IntPtr, uint>)V(p)[2])(p); p = IntPtr.Zero; }
    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(in Guid iid, out IntPtr factory);
    [DllImport("d3d11.dll")] private static extern int D3D11CreateDevice(IntPtr adapter, uint driverType, IntPtr software, uint flags, IntPtr levels, uint count, uint sdk, out IntPtr device, out uint level, out IntPtr context);
    [StructLayout(LayoutKind.Sequential)] private struct SwapDesc { public uint Width, Height, Format; public int Stereo; public uint SampleCount, SampleQuality, Usage, Count, Scaling, Effect, Alpha, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct TextureDesc { public uint Width, Height, Mips, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccess, Misc; }
    [StructLayout(LayoutKind.Sequential)] private struct MappedResource { public IntPtr Data; public uint RowPitch, DepthPitch; }
    [StructLayout(LayoutKind.Sequential)] private struct DxgiMatrix3x2 { public float M11, M12, M21, M22, M31, M32; }

    public D3DVideoSurface(SwapChainPanel panel)
    {
        try
        {
            Check(CreateDXGIFactory1(new Guid("50c83a1c-e072-4c48-87b0-3630fa36a6d0"), out _factory));
            IntPtr selected = IntPtr.Zero;
            try
            {
                byte* desc = stackalloc byte[312];
                for (uint i = 0; ; i++)
                {
                    IntPtr adapter;
                    var hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)V(_factory)[12])(_factory, i, &adapter);
                    if (hr < 0) break;
                    ((delegate* unmanaged[Stdcall]<IntPtr, void*, int>)V(adapter)[10])(adapter, desc);
                    var vendor = *(uint*)(desc + 256);
                    var name = new string((char*)desc);
                    if (selected == IntPtr.Zero || vendor == 0x10de)
                    {
                        Release(ref selected); selected = adapter; AdapterName = name;
                        if (vendor == 0x10de) break;
                    }
                    else Release(ref adapter);
                }
                if (selected == IntPtr.Zero) throw new InvalidOperationException("No graphics adapter available");
                Check(D3D11CreateDevice(selected, 0, IntPtr.Zero, 0x20, IntPtr.Zero, 0, 7, out _device, out _, out _context));
            }
            finally { Release(ref selected); }
            var unknown = WinRT.MarshalInspectable<object>.FromManaged(panel);
            try { var iid = new Guid("63aad0b8-7c24-40ff-85a8-640d944cc325"); Check(Marshal.QueryInterface(unknown, ref iid, out _panel)); }
            finally { Marshal.Release(unknown); }
            Resize(1280, 720, panel.CompositionScaleX, panel.CompositionScaleY);
            Log.Info("[Anime4K] Rendering GPU: " + AdapterName);
        }
        catch { Dispose(); throw; }
    }

    public void Resize(int width, int height, float scaleX, float scaleY)
    {
        // The back buffer uses physical pixels, while SwapChainPanel is laid out in DIPs.
        // XAML applies CompositionScale to the swap chain when composing the visual tree.
        // IDXGISwapChain2 needs the inverse transform to prevent cropping at 125%+ DPI.
        scaleX = float.IsFinite(scaleX) && scaleX > 0 ? scaleX : 1f;
        scaleY = float.IsFinite(scaleY) && scaleY > 0 ? scaleY : 1f;
        width = Math.Clamp(width, 16, 7680); height = Math.Clamp(height, 16, 4320);
        if (width == _width && height == _height && scaleX == _scaleX && scaleY == _scaleY) return;
        if (_swapChain == IntPtr.Zero)
        {
            var desc = new SwapDesc { Width = (uint)width, Height = (uint)height, Format = 87, SampleCount = 1, Usage = 0x20, Count = 2, Scaling = 0, Effect = 3, Alpha = 3 };
            IntPtr swap;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, SwapDesc*, IntPtr, IntPtr*, int>)V(_factory)[24])(_factory, _device, &desc, IntPtr.Zero, &swap));
            _swapChain = swap;
            var swapChain2Id = new Guid("a8be2ac4-199f-4946-b331-79599fb98de7");
            Check(Marshal.QueryInterface(_swapChain, ref swapChain2Id, out _swapChain2));
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)V(_panel)[3])(_panel, _swapChain));
        }
        else if (width != _width || height != _height)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, void>)V(_context)[110])(_context); // ClearState
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, uint, uint, uint, int>)V(_swapChain)[13])(_swapChain, 2, (uint)width, (uint)height, 87, 0));
        }

        // IDXGISwapChain2::SetMatrixTransform (vtable slot 34), called on every DPI change,
        // even when the rounded backing-buffer dimensions have not changed.
        var matrix = new DxgiMatrix3x2 { M11 = 1f / scaleX, M22 = 1f / scaleY };
        Check(((delegate* unmanaged[Stdcall]<IntPtr, DxgiMatrix3x2*, int>)V(_swapChain2)[34])(_swapChain2, &matrix));
        _width = width; _height = height; _scaleX = scaleX; _scaleY = scaleY;
    }

    public void Render(IntPtr renderContext)
    {
        IntPtr texture = IntPtr.Zero;
        var iid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int>)V(_swapChain)[9])(_swapChain, 0, &iid, &texture));
        try
        {
            var noWait = 0;
            MpvNative.Check(MpvNative.mpv_render_context_render(renderContext, new[] { new MpvNative.Param(22, (IntPtr)(&texture)), new MpvNative.Param(12, (IntPtr)(&noWait)), new MpvNative.Param(0, IntPtr.Zero) }));
        }
        finally { Release(ref texture); }
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, uint, int>)V(_swapChain)[8])(_swapChain, 0, 0));
        MpvNative.mpv_render_context_report_swap(renderContext);
    }

    public void Dispose()
    {
        if (_panel != IntPtr.Zero) ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)V(_panel)[3])(_panel, IntPtr.Zero);
        Release(ref _swapChain2); Release(ref _swapChain); Release(ref _panel); Release(ref _context); Release(ref _device); Release(ref _factory);
    }

    // Test-only readback of our own render target, independent of desktop capture.
    public string CaptureFrame(IntPtr renderContext, string path)
    {
        IntPtr texture = IntPtr.Zero, staging = IntPtr.Zero;
        var iid = new Guid("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
        Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int>)V(_swapChain)[9])(_swapChain, 0, &iid, &texture));
        try
        {
            var noWait = 0;
            MpvNative.Check(MpvNative.mpv_render_context_render(renderContext, new[] { new MpvNative.Param(22, (IntPtr)(&texture)), new MpvNative.Param(12, (IntPtr)(&noWait)), new MpvNative.Param(0, IntPtr.Zero) }));
            TextureDesc desc;
            ((delegate* unmanaged[Stdcall]<IntPtr, TextureDesc*, void>)V(texture)[10])(texture, &desc);
            desc.Usage = 3; desc.BindFlags = 0; desc.CPUAccess = 0x20000; desc.Misc = 0;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, TextureDesc*, IntPtr, IntPtr*, int>)V(_device)[5])(_device, &desc, IntPtr.Zero, &staging));
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, void>)V(_context)[47])(_context, staging, texture);
            MappedResource mapped;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, MappedResource*, int>)V(_context)[14])(_context, staging, 0, 1, 0, &mapped));
            var pixels = new byte[checked((int)(desc.Width * desc.Height * 4))];
            try { for (var y = 0; y < desc.Height; y++) Marshal.Copy(mapped.Data + (int)(y * mapped.RowPitch), pixels, (int)(y * desc.Width * 4), (int)(desc.Width * 4)); }
            finally { ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, void>)V(_context)[15])(_context, staging, 0); }
            using var writer = new BinaryWriter(File.Create(path));
            writer.Write((ushort)0x4d42); writer.Write(54 + pixels.Length); writer.Write(0); writer.Write(54); writer.Write(40); writer.Write((int)desc.Width); writer.Write(-(int)desc.Height); writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0); writer.Write(pixels.Length); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(pixels);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(pixels));
        }
        finally { Release(ref staging); Release(ref texture); }
    }
}
