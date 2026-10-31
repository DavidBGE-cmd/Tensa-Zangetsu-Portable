using System.Security.Cryptography;
using System.Text.Json;
using TensaZangetsu.Protocol.Abstractions;
using TensaZangetsu.Protocol.Mtk.Abstractions;
using TensaZangetsu.Protocol.Mtk.Loaders;

namespace TensaZangetsu.Services.Mtk;

/// <summary>
/// Resolves only explicitly verified MTK resources from the offline catalog.
/// File names, folder names and hashes alone never establish compatibility.
/// </summary>
public sealed class MtkCatalogResourceProvider : IMtkDaProvider, IMtkEmiProvider
{
    private const long MaximumAuthenticationLength = 1 * 1024 * 1024;
    private readonly string _suiteRoot;
    private readonly IReadOnlyList<Entry> _entries;

    public MtkCatalogResourceProvider(string suiteRoot, string? manifestPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suiteRoot);
        _suiteRoot = Path.GetFullPath(suiteRoot);
        string path = manifestPath ?? Path.Combine(_suiteRoot, "Catalog", "mtk-assets.json");
        _entries = LoadEntries(path);
    }

    public ValueTask<MtkDaImage> GetDownloadAgentAsync(
        MtkTargetInfo target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        Entry[] candidates = _entries
            .Where(entry => entry.Kind == "da" && entry.VerificationStatus == "Verified")
            .Where(entry => entry.Supports(target))
            .ToArray();

        if (candidates.Length == 0)
            throw new MtkResourceException("verified compatible DA");

        MtkDaImage? selected = null;
        foreach (Entry candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IDataSource source = OpenVerifiedSource(candidate);
            MtkDaImage image;
            try
            {
                image = MtkDaParser.Select(source, target, candidate.DaKind);
            }
            catch (MtkResourceException)
            {
                continue;
            }

            if (selected is not null)
                throw new MtkResourceException("ambiguous verified DA");
            selected = image;
        }

        return selected is null
            ? throw new MtkResourceException("verified compatible DA")
            : ValueTask.FromResult(selected);
    }

    public ValueTask<MtkEmiImage?> GetEmiAsync(
        MtkTargetInfo target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        Entry[] candidates = _entries
            .Where(entry => entry.Kind == "emi" && entry.VerificationStatus == "Verified")
            .Where(entry => entry.Supports(target))
            .ToArray();
        if (candidates.Length == 0)
            return ValueTask.FromResult<MtkEmiImage?>(null);
        if (candidates.Length > 1)
            throw new MtkResourceException("ambiguous verified EMI");

        return ValueTask.FromResult<MtkEmiImage?>(
            MtkEmiParser.Parse(OpenVerifiedSource(candidates[0])));
    }

    /// <summary>
    /// Loads a verified static Auth/certificate buffer for explicit host wiring.
    /// This method never creates a signer or generates an authentication response.
    /// </summary>
    public MtkSensitiveBuffer OpenVerifiedAuthentication(MtkTargetInfo target, string kind)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);

        Entry[] candidates = _entries
            .Where(entry => entry.Kind == kind && entry.VerificationStatus == "Verified")
            .Where(entry => entry.Supports(target))
            .ToArray();
        if (candidates.Length != 1)
            throw new MtkResourceException(candidates.Length == 0
                ? $"verified compatible {kind}"
                : $"ambiguous verified {kind}");

        string path = ResolveContainedPath(candidates[0].ImportedRelativePath);
        if (candidates[0].Length > MaximumAuthenticationLength)
            throw new MtkResourceException("authentication resource length");
        byte[] buffer = File.ReadAllBytes(path);
        try
        {
            ValidateFile(candidates[0], path, buffer.LongLength);
            return new MtkSensitiveBuffer(buffer);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(buffer);
            throw;
        }
    }

    public bool HasVerifiedAuthentication(MtkTargetInfo target, string kind)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        return _entries.Any(entry =>
            entry.Kind == kind &&
            entry.VerificationStatus == "Verified" &&
            entry.Supports(target));
    }

    private IDataSource OpenVerifiedSource(Entry entry)
    {
        string path = ResolveContainedPath(entry.ImportedRelativePath);
        ValidateFile(entry, path, new FileInfo(path).Length);
        return new VerifiedFileDataSource(path, entry.Length);
    }

    private void ValidateFile(Entry entry, string path, long actualLength)
    {
        if (actualLength != entry.Length)
            throw new MtkResourceException("catalog resource length");

        string hash;
        using (FileStream stream = File.OpenRead(path))
            hash = Convert.ToHexString(SHA256.HashData(stream));
        if (!hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new MtkResourceException("catalog resource hash");
    }

    private string ResolveContainedPath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new MtkResourceException("catalog resource path");
        string root = _suiteRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        string full = Path.GetFullPath(Path.Combine(_suiteRoot, relativePath));
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new MtkResourceException("catalog resource path");
        if (!File.Exists(full))
            throw new MtkResourceException("catalog resource missing");
        return full;
    }

    private static IReadOnlyList<Entry> LoadEntries(string manifestPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!document.RootElement.TryGetProperty("schemaVersion", out JsonElement schema) ||
            schema.GetInt32() != 1)
            throw new MtkResourceException("catalog schema");
        if (!document.RootElement.TryGetProperty("models", out JsonElement models))
            throw new MtkResourceException("catalog models");

        var result = new List<Entry>();
        foreach (JsonElement model in models.EnumerateArray())
        {
            string modelId = model.GetProperty("id").GetString() ?? throw new MtkResourceException("catalog model");
            foreach (JsonElement asset in model.GetProperty("assets").EnumerateArray())
            {
                result.Add(new(
                    modelId,
                    asset.GetProperty("kind").GetString() ?? "",
                    asset.GetProperty("importedRelativePath").GetString() ?? "",
                    asset.GetProperty("length").GetInt64(),
                    asset.GetProperty("sha256").GetString() ?? "",
                    asset.GetProperty("verificationStatus").GetString() ?? "",
                    ReadUInt16Array(asset, "hardwareCodes"),
                    ReadUInt16Array(asset, "hardwareSubCodes"),
                    ReadDaKind(asset)));
            }
        }
        return result.AsReadOnly();
    }

    private static ushort[] ReadUInt16Array(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out JsonElement values) ||
            values.ValueKind != JsonValueKind.Array)
            return [];
        return values.EnumerateArray().Select(value =>
            checked((ushort)value.GetUInt32())).ToArray();
    }

    private static MtkDaKind? ReadDaKind(JsonElement element) =>
        element.TryGetProperty("daKind", out JsonElement value) &&
        Enum.TryParse(value.GetString(), true, out MtkDaKind kind) ? kind : null;

    private sealed record Entry(
        string Model,
        string Kind,
        string ImportedRelativePath,
        long Length,
        string Sha256,
        string VerificationStatus,
        IReadOnlyList<ushort> HardwareCodes,
        IReadOnlyList<ushort> HardwareSubCodes,
        MtkDaKind? DaKind)
    {
        public bool Supports(MtkTargetInfo target) =>
            (HardwareCodes.Count == 0 || HardwareCodes.Contains(target.HardwareCode)) &&
            (HardwareSubCodes.Count == 0 || HardwareSubCodes.Contains(target.HardwareSubCode));
    }

    private sealed class VerifiedFileDataSource(string path, long length) : IDataSource
    {
        public long Length => length;
        public Stream OpenStream() => File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        public ValueTask<Stream> OpenStreamAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<Stream>(OpenStream());
        }
    }
}
