using TensaZangetsu.Protocol.Mtk.Abstractions;

namespace TensaZangetsu.Services.Mtk;

/// <summary>Immutable, read-only request prepared against one observed MTK session.</summary>
public sealed record MtkReadPlan(long Generation, MtkFlashRange Range, string Description)
{
    public static MtkReadPlan Create(
        IMtkProtocol protocol,
        MtkFlashRange range,
        string description = "MTK storage read")
    {
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (protocol.SessionState != MtkSessionState.StorageReady)
            throw new InvalidOperationException("MTK session is not ready for reading.");
        if (range.Offset < 0 || range.Length <= 0)
            throw new ArgumentOutOfRangeException(nameof(range));

        MtkStorageInfo storage = protocol.GetStorageInfo();
        MtkStorageRegion region = storage.Regions.SingleOrDefault(r => r.WireId == range.RegionId)
            ?? throw new MtkResourceException("read region");
        if (range.Offset % region.BlockSize != 0 ||
            range.Length % region.BlockSize != 0 ||
            range.Offset > region.Length - range.Length)
            throw new ArgumentOutOfRangeException(nameof(range));

        return new(protocol.Generation, range, description);
    }

    public void Validate(IMtkProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        if (protocol.Generation != Generation || protocol.SessionState != MtkSessionState.StorageReady)
            throw new InvalidOperationException("MTK session changed; recreate the read plan.");
    }
}
