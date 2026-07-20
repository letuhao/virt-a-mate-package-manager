using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using VarVault.Common;
using VarVault.Domain.Entities;
using VarVault.Domain.Repositories;

namespace VarVault.Infrastructure.Repositories;

/// <summary>
/// Windows drive profiler. Media type = <see cref="DriveInfo"/> drive-type + a seek-penalty query
/// (SSD vs HDD) + bus-type query (NVMe); any failure falls back to HDD. Capacity via
/// <see cref="DriveInfo"/>; volume serial via <c>GetVolumeInformation</c>. (BE-R2/R3/R4.)
/// </summary>
public sealed class DriveProfiler : IDriveProfiler
{
    public DriveProfile Profile(string path)
    {
        Guard.NotNullOrWhiteSpace(path);
        var (capacity, free) = GetCapacity(path);
        return new DriveProfile(DetectMediaType(path), capacity, free, ReadVolumeSerial(path));
    }

    public (long? CapacityBytes, long? FreeBytes) GetCapacity(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
                return (null, null);
            var info = new DriveInfo(root);
            if (!info.IsReady)
                return (null, null);
            return (info.TotalSize, info.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return (null, null);
        }
    }

    private static MediaType DetectMediaType(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
                return MediaType.Unknown;

            var driveType = new DriveInfo(root).DriveType;
            switch (driveType)
            {
                case DriveType.Removable: return MediaType.Removable;
                case DriveType.Network: return MediaType.Network;
                case DriveType.CDRom: return MediaType.Removable;
                case DriveType.Fixed: break;
                default: return MediaType.Unknown;
            }

            if (!OperatingSystem.IsWindows())
                return MediaType.Hdd; // safe default off-Windows

            var letter = root.TrimEnd('\\', '/');
            using var handle = Native.OpenVolume(letter);
            if (handle.IsInvalid)
                return MediaType.Hdd;

            if (Native.IsNvme(handle))
                return MediaType.Nvme;

            var seekPenalty = Native.IncursSeekPenalty(handle);
            return seekPenalty switch
            {
                false => MediaType.Ssd,
                true => MediaType.Hdd,
                _ => MediaType.Hdd, // unknown → HDD
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return MediaType.Hdd;
        }
    }

    private static string? ReadVolumeSerial(string path)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
                return null;
            return Native.GetVolumeSerial(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}

internal static partial class Native
{
    private const uint GenericRead = 0x80000000;
    private const uint FileShareReadWrite = 0x00000001 | 0x00000002;
    private const uint OpenExisting = 3;
    private const uint IoctlStorageQueryProperty = 0x002D1400;

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial SafeFileHandleNative CreateFile(
        string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
        uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(
        SafeFileHandleNative hDevice, uint dwIoControlCode,
        ref StoragePropertyQuery lpInBuffer, int nInBufferSize,
        IntPtr lpOutBuffer, int nOutBufferSize, out int lpBytesReturned, IntPtr lpOverlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformation(
        string lpRootPathName, IntPtr lpVolumeNameBuffer, int nVolumeNameSize,
        out uint lpVolumeSerialNumber, out uint lpMaximumComponentLength, out uint lpFileSystemFlags,
        IntPtr lpFileSystemNameBuffer, int nFileSystemNameSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct StoragePropertyQuery
    {
        public int PropertyId;
        public int QueryType;
        public byte AdditionalParameters;
    }

    // Open with desiredAccess = 0 (metadata/query level). IOCTL_STORAGE_QUERY_PROPERTY works at this level WITHOUT
    // administrator — opening the volume with GENERIC_READ would require elevation, and (since the app runs asInvoker)
    // that failure previously made every drive fall back to HDD, mis-detecting SSDs. (bugfix.)
    public static SafeFileHandleNative OpenVolume(string driveLetter) =>
        CreateFile($@"\\.\{driveLetter}", 0, FileShareReadWrite, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);

    // StorageDeviceSeekPenaltyProperty = 7. DEVICE_SEEK_PENALTY_DESCRIPTOR: version+size (8 bytes) + bool.
    public static bool? IncursSeekPenalty(SafeFileHandleNative handle)
    {
        var query = new StoragePropertyQuery { PropertyId = 7, QueryType = 0 };
        var size = Marshal.SizeOf<StoragePropertyQuery>() + 16;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!DeviceIoControl(handle, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(), buffer, size, out _, IntPtr.Zero))
                return null;
            // offset 8 = IncursSeekPenalty (BOOLEAN)
            return Marshal.ReadByte(buffer, 8) != 0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // StorageAdapterProperty = 6. STORAGE_ADAPTER_DESCRIPTOR BusType at offset 24 (int). BusTypeNvme = 0x11.
    public static bool IsNvme(SafeFileHandleNative handle)
    {
        var query = new StoragePropertyQuery { PropertyId = 6, QueryType = 0 };
        var size = 1024;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!DeviceIoControl(handle, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(), buffer, size, out var returned, IntPtr.Zero) || returned < 28)
                return false;
            var busType = Marshal.ReadInt32(buffer, 24);
            return busType == 0x11;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static string? GetVolumeSerial(string root)
    {
        var withSep = root.EndsWith('\\') ? root : root + "\\";
        if (GetVolumeInformation(withSep, IntPtr.Zero, 0, out var serial, out _, out _, IntPtr.Zero, 0))
            return serial.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
        return null;
    }
}

internal sealed class SafeFileHandleNative() : SafeHandle(IntPtr.Zero, ownsHandle: true)
{
    public override bool IsInvalid => handle == IntPtr.Zero || handle == new IntPtr(-1);

    protected override bool ReleaseHandle() => Native_CloseHandle(handle);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool Native_CloseHandle(IntPtr handle);
}
