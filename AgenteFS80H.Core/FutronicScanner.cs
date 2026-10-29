using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Futureplace.AgenteFS80H;

/// <summary>ABI verificada no ftrScanAPI.h do fabricante; binário proprietário é selecionado pelo usuário.</summary>
public sealed class FutronicScanner : IFingerprintScanner
{
    private IntPtr library;
    private IntPtr device;
    private readonly CloseDelegate close;
    private readonly PresenceDelegate presence;
    private readonly FrameDelegate getFrame;
    private readonly ImageSize size;
    private const int EmptyFrame = 4306;

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct ImageSize { public int Width; public int Height; public int Bytes; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)] private delegate IntPtr OpenDelegate();
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)] private delegate void CloseDelegate(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)] private delegate int SizeDelegate(IntPtr handle, out ImageSize value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)] private delegate int OptionsDelegate(IntPtr handle, uint mask, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)] private delegate int PresenceDelegate(IntPtr handle, IntPtr reserved);
    [UnmanagedFunctionPointer(CallingConvention.StdCall, SetLastError = true)] private delegate int FrameDelegate(IntPtr handle, [Out] byte[] buffer, IntPtr reserved);

    public FutronicScanner(string path)
    {
        ValidateLibrary(path);
        try
        {
            library = NativeLibrary.Load(Path.GetFullPath(path), typeof(FutronicScanner).Assembly, DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
            var open = Export<OpenDelegate>("ftrScanOpenDevice");
            close = Export<CloseDelegate>("ftrScanCloseDevice");
            presence = Export<PresenceDelegate>("ftrScanIsFingerPresent");
            getFrame = Export<FrameDelegate>("ftrScanGetFrame");
            var getSize = Export<SizeDelegate>("ftrScanGetImageSize");
            var options = Export<OptionsDelegate>("ftrScanSetOptions");
            device = open();
            if (device == IntPtr.Zero) throw new CaptureException("captura");
            // LFD obrigatório. Nunca continuar silenciosamente se hardware/firmware não o suportar.
            if (options(device, 0x1, 0x1) == 0 || getSize(device, out size) == 0) throw new CaptureException("captura");
            if (size.Width != 320 || size.Height != 480 || size.Bytes != 320 * 480) throw new CaptureException("captura");
        }
        catch
        {
            Dispose();
            throw;
        }
    }
    internal static void ValidateLibrary(string path)
    {
        if (!Environment.Is64BitProcess || !Path.IsPathFullyQualified(path) || !string.Equals(Path.GetFileName(path), "ftrScanAPI.dll", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new CaptureException("captura");
        try
        {
            using var file = File.OpenRead(path);
            using var reader = new BinaryReader(file);
            if (reader.ReadUInt16() != 0x5a4d || file.Length < 64) throw new CaptureException("captura");
            file.Position = 0x3c;
            var offset = reader.ReadInt32();
            if (offset < 64 || offset > file.Length - 6) throw new CaptureException("captura");
            file.Position = offset;
            if (reader.ReadUInt32() != 0x4550 || reader.ReadUInt16() != 0x8664) throw new CaptureException("captura");
        }
        catch (IOException) { throw new CaptureException("captura"); }
    }
    private T Export<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
    public bool IsFingerPresent()
    {
        ObjectDisposedException.ThrowIf(device == IntPtr.Zero, this);
        if (presence(device, IntPtr.Zero) != 0) return true;
        var error = Marshal.GetLastPInvokeError();
        if (error == EmptyFrame) return false;
        throw new CaptureException("captura");
    }
    public FingerprintFrame Capture()
    {
        ObjectDisposedException.ThrowIf(device == IntPtr.Zero, this);
        var pixels = new byte[size.Bytes];
        if (getFrame(device, pixels, IntPtr.Zero) != 0) return new(size.Width, size.Height, pixels);
        var error = Marshal.GetLastPInvokeError();
        CryptographicOperations.ZeroMemory(pixels);
        throw new CaptureException(error == 0x20000001 ? "inconsistente" : "captura");
    }
    public void Dispose()
    {
        if (device != IntPtr.Zero) { close?.Invoke(device); device = IntPtr.Zero; }
        if (library != IntPtr.Zero) { NativeLibrary.Free(library); library = IntPtr.Zero; }
    }
}
