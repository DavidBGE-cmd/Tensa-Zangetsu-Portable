namespace TensaZangetsu.AssetIndexing;

public enum AssetKind
{
    Unknown,
    BinaryUnknown,
    MtkDownloadAgentCandidate,
    PreloaderCandidate,
    FirehoseProgrammerCandidate,
    SprdFdlCandidate,
    Scatter,
    FirehoseRawProgram,
    FirehosePatch,
    PacPackageCandidate,
    SamsungPitCandidate,
    FirmwareArchiveCandidate,
    TestPointImage,
    Image,
    MetadataSidecar
}

public enum VerificationStatus
{
    ReferenceOnly,
    IndexedUnverified,
    ImplementedUntested,
    Verified,
    Unavailable
}

public sealed record DeviceCatalogEntry
{
    public string Id { get; init; } = string.Empty;
    public string Brand { get; init; } = string.Empty;
    public string MarketingName { get; init; } = string.Empty;
    public string[] ModelCodes { get; init; } = [];
    public string[] CodeNames { get; init; } = [];
    public string? Platform { get; init; }
    public string? Soc { get; init; }
    public string[] BoardRevisions { get; init; } = [];
    public string[] StorageTypes { get; init; } = [];
    public string[] ConnectionModes { get; init; } = [];
    public string[] SourceReferences { get; init; } = [];
    public VerificationStatus VerificationStatus { get; init; } = VerificationStatus.ReferenceOnly;
    public OperationCapability[] Capabilities { get; init; } = [];
}

public sealed record OperationCapability
{
    public string OperationId { get; init; } = string.Empty;
    public VerificationStatus ImplementationStatus { get; init; } = VerificationStatus.Unavailable;
    public string[] CompatibleModes { get; init; } = [];
    public string[] SecurityConstraints { get; init; } = [];
    public string[] FirmwareConstraints { get; init; } = [];
    public string? BackendId { get; init; }
    public string[] Evidence { get; init; } = [];
    public DateTimeOffset? LastVerifiedAt { get; init; }
}

public record AssetMetadata
{
    public int SchemaVersion { get; init; } = 1;
    public AssetKind? Kind { get; init; }
    public string? Brand { get; init; }
    public string? Soc { get; init; }
    public string[]? ModelCodes { get; init; }
    public string? BoardRevision { get; init; }
    public string? ProtocolFamily { get; init; }
    public string? BitConstraint { get; init; }
    public string? AndroidConstraint { get; init; }
    public string? HyperOsConstraint { get; init; }
    public string? StorageConstraint { get; init; }
    public string? BundleId { get; init; }
    public string? BundleRole { get; init; }
    public string? Provenance { get; init; }
    public VerificationStatus? VerificationStatus { get; init; }
}

public sealed record AssetMetadataOverride : AssetMetadata
{
    public string RelativePath { get; init; } = string.Empty;
}

public sealed record AssetEntry
{
    public string Id { get; init; } = string.Empty;
    public string RootId { get; init; } = string.Empty;
    public string RelativePath { get; init; } = string.Empty;
    public long Length { get; init; }
    public DateTimeOffset LastWriteTimeUtc { get; init; }
    public string Sha256 { get; init; } = string.Empty;
    public AssetKind Kind { get; init; }
    public VerificationStatus VerificationStatus { get; init; } = VerificationStatus.IndexedUnverified;
    public string MetadataSource { get; init; } = "content";
    public string? Brand { get; init; }
    public string? Soc { get; init; }
    public string[] ModelCodes { get; init; } = [];
    public string? BoardRevision { get; init; }
    public string? ProtocolFamily { get; init; }
    public string? BitConstraint { get; init; }
    public string? AndroidConstraint { get; init; }
    public string? HyperOsConstraint { get; init; }
    public string? StorageConstraint { get; init; }
    public string? BundleId { get; init; }
    public string? BundleRole { get; init; }
    public string? Provenance { get; init; }
}

public sealed record AssetBundle(string Id, AssetEntry[] Members);

public sealed record AssetDuplicateGroup(string Sha256, string[] RelativePaths);

public sealed record AssetDiagnostic(
    string Severity,
    string Code,
    string? RelativePath,
    string Message);

public sealed record AssetsManifest
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset GeneratedAt { get; init; }
    public string[] SourceRoots { get; init; } = [];
    public AssetEntry[] Assets { get; init; } = [];
    public AssetBundle[] Bundles { get; init; } = [];
    public AssetDuplicateGroup[] DuplicateGroups { get; init; } = [];
    public AssetEntry[] UnifiedPreloadersDb { get; init; } = [];
    public AssetDiagnostic[] Diagnostics { get; init; } = [];
}

public sealed record AssetIndexRoot(string Id, string Path);

public sealed record AssetIndexOptions(
    AssetIndexRoot[] AssetRoots,
    string DeviceCatalogPath,
    string OverridesPath,
    string OutputPath,
    bool Rehash = false);

public sealed record AssetIndexProgress(int FilesProcessed, string RelativePath);

public sealed record AssetIndexResult(
    string ManifestPath,
    int FilesIndexed,
    int FilesFailed,
    int DuplicateGroups,
    int Bundles,
    int Diagnostics);
