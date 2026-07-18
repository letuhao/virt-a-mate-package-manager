namespace VarVault.Domain.Entities;

/// <summary>A registered storage location (a folder on a drive) that holds var files. (Data-arch §3 Storage.)</summary>
public sealed class Repository
{
    /// <summary>Stable GUID identity — survives drive-letter changes; only <see cref="MountPath"/> moves.</summary>
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string MountPath { get; set; } = string.Empty;

    /// <summary>Volume serial captured at registration; enforced on re-point (mismatch → read-only + prompt).</summary>
    public string? VolumeSerial { get; set; }

    public MediaType MediaType { get; set; } = MediaType.Unknown;
    public int Tier { get; set; }
    public int PriorityInTier { get; set; }

    public double? ReadSpeedMBps { get; set; }
    public double? WriteSpeedMBps { get; set; }
    public DateTime? BenchmarkedAt { get; set; }

    public long? CapacityBytes { get; set; }
    public long? FreeBytes { get; set; }
    public long MinFreeBytes { get; set; }

    public bool IsEnabled { get; set; } = true;
    public bool IsOnline { get; set; } = true;
    public bool IsReadOnly { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<VarFile> VarFiles { get; } = new List<VarFile>();
}
