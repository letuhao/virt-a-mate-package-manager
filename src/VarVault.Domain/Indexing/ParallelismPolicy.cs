using VarVault.Domain.Entities;

namespace VarVault.Domain.Indexing;

/// <summary>
/// Per-physical-drive scan concurrency: high on NVMe/SSD, 1 on HDD (avoid head thrash), modest on
/// network, 1 on removable. Unknown media is treated as HDD (safe). Work is grouped by physical drive,
/// not per-repo, so two repos on one HDD share a single lane. (IDX-3, checklist 1.24.)
/// </summary>
public static class ParallelismPolicy
{
    public static int DegreeFor(MediaType mediaType) => mediaType switch
    {
        MediaType.Nvme => 8,
        MediaType.Ssd => 4,
        MediaType.Network => 2,
        MediaType.Removable => 1,
        _ => 1, // HDD / Unknown — one lane, no random-read thrash
    };
}
