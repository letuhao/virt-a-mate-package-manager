using VarVault.Domain.Entities;

namespace VarVault.Domain.Repositories;

/// <summary>Physical facts about the drive backing a path.</summary>
public sealed record DriveProfile(
    MediaType MediaType,
    long? CapacityBytes,
    long? FreeBytes,
    string? VolumeSerial);

/// <summary>
/// Profiles the drive behind a repository path: media type (NVMe/SSD/HDD/Removable/Network), live
/// capacity, and the volume serial. Implemented by Infrastructure (Win32); unknown media falls back
/// to HDD (the safe/slow assumption). (Checklist BE-R2/R3/R4.)
/// </summary>
public interface IDriveProfiler
{
    DriveProfile Profile(string path);

    /// <summary>Live free/total capacity only (cheap; for on-demand refresh). (BE-R3.)</summary>
    (long? CapacityBytes, long? FreeBytes) GetCapacity(string path);
}
