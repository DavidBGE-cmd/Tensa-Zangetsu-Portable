using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;

namespace TensaZangetsu.AssetIndexing;

public sealed class AssetIndexingService
{
    private const long MaximumXmlCharacters = 16 * 1024 * 1024;
    private const int MaximumScatterBytes = 512 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public async Task<AssetIndexResult> IndexAsync(
        AssetIndexOptions options,
        IProgress<AssetIndexProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.AssetRoots is null || options.AssetRoots.Length == 0)
            throw new InvalidDataException("At least one asset root is required.");
        var roots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (AssetIndexRoot assetRoot in options.AssetRoots)
        {
            if (!IsValidRootId(assetRoot.Id) || !roots.TryAdd(assetRoot.Id, Path.GetFullPath(assetRoot.Path)))
                throw new InvalidDataException($"Invalid or duplicate asset root id: {assetRoot.Id}");
            if (!Directory.Exists(roots[assetRoot.Id]))
                throw new DirectoryNotFoundException($"Assets root does not exist: {roots[assetRoot.Id]}");
            if ((File.GetAttributes(roots[assetRoot.Id]) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"The assets root '{assetRoot.Id}' cannot be a symbolic link or reparse point.");
        }

        string deviceCatalogPath = Path.GetFullPath(options.DeviceCatalogPath);
        string overridesPath = Path.GetFullPath(options.OverridesPath);
        string outputPath = Path.GetFullPath(options.OutputPath);
        string outputDirectory = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidDataException("The output path must have a parent directory.");

        if (PathComparer.Equals(outputPath, deviceCatalogPath) || PathComparer.Equals(outputPath, overridesPath))
            throw new InvalidDataException("Output path must not overwrite the device catalog or overrides.");

        _ = await ReadDeviceCatalogAsync(deviceCatalogPath, cancellationToken);
        Dictionary<string, AssetMetadataOverride> overrides = await ReadOverridesAsync(overridesPath, roots, cancellationToken);
        Dictionary<string, AssetEntry> previousEntries = [];
        AssetDiagnostic? diagnosticsForPreviousManifest = null;
        if (!options.Rehash && File.Exists(outputPath))
        {
            try
            {
                AssetsManifest previous = await ReadAndValidateManifestAsync(outputPath, cancellationToken);
                previousEntries = previous.Assets.ToDictionary(entry => entry.RelativePath, PathComparer);
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
            {
                diagnosticsForPreviousManifest = new AssetDiagnostic(
                    "warning", "PreviousManifestIgnored", null, $"Previous manifest was ignored ({ex.GetType().Name}).");
            }
        }

        Directory.CreateDirectory(outputDirectory);
        var outputPaths = new HashSet<string>(PathComparer)
        {
            outputPath,
            outputPath + ".tmp"
        };
        var diagnostics = new List<AssetDiagnostic>();
        if (diagnosticsForPreviousManifest is not null)
            diagnostics.Add(diagnosticsForPreviousManifest);
        var entries = new List<AssetEntry>();
        int failed = 0;
        int processed = 0;

        foreach ((string rootId, string root) in roots.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(root);
            while (pendingDirectories.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = pendingDirectories.Pop();
                IEnumerable<string> paths;
                try
                {
                    paths = Directory.EnumerateFileSystemEntries(directory).ToArray();
                }
                catch (Exception ex) when (IsFileSystemException(ex))
                {
                    diagnostics.Add(new AssetDiagnostic(
                        "error", "DirectoryReadFailed", ToManifestPath(rootId, root, directory), $"Directory could not be read ({ex.GetType().Name})."));
                    failed++;
                    continue;
                }

                foreach (string path in paths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string relativePath;
                    try
                    {
                        relativePath = ToManifestPath(rootId, root, path);
                    }
                    catch (InvalidDataException)
                    {
                        diagnostics.Add(new AssetDiagnostic("error", "PathEscapesRoot", null, "An entry resolved outside its configured root."));
                        failed++;
                        continue;
                    }

                    try
                    {
                        FileAttributes attributes = File.GetAttributes(path);
                        if ((attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            diagnostics.Add(new AssetDiagnostic(
                                "warning", "ReparsePointSkipped", relativePath, "Symbolic link or reparse point was not followed."));
                            continue;
                        }

                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            pendingDirectories.Push(path);
                            continue;
                        }

                        string fullPath = Path.GetFullPath(path);
                        if (outputPaths.Contains(fullPath))
                            continue;

                        previousEntries.TryGetValue(relativePath, out AssetEntry? previousEntry);
                        AssetEntry entry = await CreateEntryAsync(
                            rootId, root, fullPath, relativePath, previousEntry, options.Rehash, cancellationToken, diagnostics);

                        if (entry.Kind != AssetKind.MetadataSidecar)
                        {
                            string sidecarPath = fullPath + ".metadata.json";
                            if (File.Exists(sidecarPath))
                            {
                                AssetMetadata? sidecar = await TryReadSidecarAsync(
                                    root, sidecarPath, cancellationToken, diagnostics);
                                if (sidecar is not null)
                                    entry = ApplyMetadata(entry, sidecar, "sidecar", relativePath, diagnostics);
                            }

                            if (overrides.TryGetValue(relativePath, out AssetMetadataOverride? assetOverride))
                                entry = ApplyMetadata(entry, assetOverride, "override", relativePath, diagnostics);
                        }

                        entries.Add(entry);
                        processed++;
                        progress?.Report(new AssetIndexProgress(processed, relativePath));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex) when (IsFileSystemException(ex) || ex is CryptographicException)
                    {
                        diagnostics.Add(new AssetDiagnostic("error", "FileReadFailed", relativePath, $"File could not be read ({ex.GetType().Name})."));
                        failed++;
                    }
                }
            }
        }

        var indexedPaths = new HashSet<string>(entries.Select(entry => entry.RelativePath), PathComparer);
        string[] unconsumedOverrides = overrides.Keys
            .Where(path => !indexedPaths.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        foreach (string path in unconsumedOverrides)
            diagnostics.Add(new AssetDiagnostic("warning", "OverrideTargetMissing", path, "The override does not match an indexed file."));

        AssetEntry[] sortedEntries = entries
            .OrderBy(entry => entry.RelativePath, PathComparer)
            .ThenBy(entry => entry.RelativePath, StringComparer.Ordinal)
            .ToArray();
        AssetsManifest manifest = CreateManifest(roots.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray(), sortedEntries, diagnostics);
        ValidateManifest(manifest);
        await WriteAtomicallyAsync(outputPath, manifest, cancellationToken);

        return new AssetIndexResult(
            outputPath,
            processed,
            failed,
            manifest.DuplicateGroups.Length,
            manifest.Bundles.Length,
            manifest.Diagnostics.Length);
    }

    public static async Task<AssetsManifest> ReadAndValidateManifestAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = File.OpenRead(manifestPath);
        AssetsManifest? manifest = await JsonSerializer.DeserializeAsync<AssetsManifest>(
            stream, JsonOptions, cancellationToken);
        if (manifest is null)
            throw new InvalidDataException("The manifest is empty or invalid.");
        ValidateManifest(manifest);
        return manifest;
    }

    private static async Task<DeviceCatalogEntry[]> ReadDeviceCatalogAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("The device catalog must be a JSON array.");
        foreach (JsonElement element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !HasNonEmptyString(element, "id") ||
                !HasNonEmptyString(element, "brand") ||
                !HasNonEmptyString(element, "marketingName") ||
                !HasArray(element, "modelCodes") ||
                !HasArray(element, "codeNames") ||
                !HasArray(element, "boardRevisions") ||
                !HasArray(element, "storageTypes") ||
                !HasArray(element, "connectionModes") ||
                !HasArray(element, "sourceReferences") ||
                !HasArray(element, "capabilities") ||
                !TryReadStatus(element, "verificationStatus", out _))
            {
                throw new InvalidDataException("A device catalog entry is missing required fields or has invalid values.");
            }

            foreach (string property in new[] { "modelCodes", "codeNames", "boardRevisions", "storageTypes", "connectionModes", "sourceReferences" })
            {
                if (element.GetProperty(property).EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String))
                    throw new InvalidDataException($"Device catalog field '{property}' must contain strings.");
            }
        }

        DeviceCatalogEntry[]? devices = document.RootElement.Deserialize<DeviceCatalogEntry[]>(JsonOptions);
        if (devices is null)
            throw new InvalidDataException("The device catalog could not be read.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (DeviceCatalogEntry device in devices)
        {
            if (string.IsNullOrWhiteSpace(device.Id) ||
                string.IsNullOrWhiteSpace(device.Brand) ||
                string.IsNullOrWhiteSpace(device.MarketingName) ||
                !ids.Add(device.Id))
            {
                throw new InvalidDataException("Every device requires a unique id, brand, and marketingName.");
            }
            if (device.ModelCodes is null || device.CodeNames is null || device.BoardRevisions is null ||
                device.StorageTypes is null || device.ConnectionModes is null || device.SourceReferences is null ||
                device.Capabilities is null)
            {
                throw new InvalidDataException($"Device catalog entry '{device.Id}' has null collections.");
            }
        }

        return devices;
    }

    private static async Task<Dictionary<string, AssetMetadataOverride>> ReadOverridesAsync(
        string path,
        IReadOnlyDictionary<string, string> roots,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        AssetMetadataOverride[]? overrides = await JsonSerializer.DeserializeAsync<AssetMetadataOverride[]>(
            stream, JsonOptions, cancellationToken);
        if (overrides is null)
            throw new InvalidDataException("The overrides file must be a JSON array.");

        var result = new Dictionary<string, AssetMetadataOverride>(PathComparer);
        foreach (AssetMetadataOverride assetOverride in overrides)
        {
            string relativePath = NormalizeRelativePath(assetOverride.RelativePath);
            int separator = relativePath.IndexOf('/');
            if (separator <= 0 ||
                !roots.TryGetValue(relativePath[..separator], out string? root))
            {
                throw new InvalidDataException($"Override path must start with a configured root id: {relativePath}");
            }
            _ = ResolveContainedPath(root, relativePath[(separator + 1)..]);
            if (assetOverride.SchemaVersion != 1)
                throw new InvalidDataException($"Unsupported override schema version for '{relativePath}'.");
            if (!result.TryAdd(relativePath, assetOverride with { RelativePath = relativePath }))
                throw new InvalidDataException($"Duplicate override path: {relativePath}");
        }

        return result;
    }

    private static async Task<AssetEntry> CreateEntryAsync(
        string rootId,
        string root,
        string fullPath,
        string relativePath,
        AssetEntry? previousEntry,
        bool rehash,
        CancellationToken cancellationToken,
        List<AssetDiagnostic> diagnostics)
    {
        var file = new FileInfo(fullPath);
        file.Refresh();
        long length = file.Length;
        var lastWriteTimeUtc = new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero);
        AssetKind kind = await ClassifyAsync(fullPath, relativePath, cancellationToken, diagnostics);
        file.Refresh();
        if (file.Length != length || file.LastWriteTimeUtc != lastWriteTimeUtc.UtcDateTime)
            throw new IOException("File changed while it was being indexed.");
        string sha256;
        if (!rehash &&
            previousEntry is not null &&
            previousEntry.Length == length &&
            previousEntry.LastWriteTimeUtc == lastWriteTimeUtc)
        {
            sha256 = previousEntry.Sha256;
        }
        else
        {
            await using FileStream stream = new(
                fullPath, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
            sha256 = Convert.ToHexString(hash).ToLowerInvariant();
            if (stream.Length != length || file.LastWriteTimeUtc != lastWriteTimeUtc.UtcDateTime)
                throw new IOException("File changed while it was being hashed.");
        }
        string id = "asset-" + Convert.ToHexString(
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(relativePath))).ToLowerInvariant()[..20];

        return new AssetEntry
        {
            Id = id,
            RootId = rootId,
            RelativePath = ToManifestPath(rootId, root, fullPath),
            Length = length,
            LastWriteTimeUtc = lastWriteTimeUtc,
            Sha256 = sha256,
            Kind = kind,
            VerificationStatus = VerificationStatus.IndexedUnverified,
            MetadataSource = "content"
        };
    }

    private static async Task<AssetKind> ClassifyAsync(
        string fullPath,
        string relativePath,
        CancellationToken cancellationToken,
        List<AssetDiagnostic> diagnostics)
    {
        string extension = Path.GetExtension(fullPath);
        if (relativePath.EndsWith(".metadata.json", StringComparison.OrdinalIgnoreCase))
            return AssetKind.MetadataSidecar;

        if (extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            return await ClassifyXmlAsync(fullPath, relativePath, cancellationToken, diagnostics);

        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
            await LooksLikeScatterAsync(fullPath, cancellationToken))
            return AssetKind.Scatter;

        string fileName = Path.GetFileName(fullPath);
        if (fileName.Contains("preloader", StringComparison.OrdinalIgnoreCase) ||
            relativePath.Contains("/preloader", StringComparison.OrdinalIgnoreCase))
            return AssetKind.PreloaderCandidate;
        if (extension.Equals(".mtk", StringComparison.OrdinalIgnoreCase))
            return AssetKind.MtkDownloadAgentCandidate;
        if (extension.Equals(".mbn", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".elf", StringComparison.OrdinalIgnoreCase))
            return AssetKind.FirehoseProgrammerCandidate;
        if (extension.Equals(".ldr", StringComparison.OrdinalIgnoreCase))
            return AssetKind.SprdFdlCandidate;
        if (extension.Equals(".pac", StringComparison.OrdinalIgnoreCase))
            return AssetKind.PacPackageCandidate;
        if (extension.Equals(".pit", StringComparison.OrdinalIgnoreCase))
            return AssetKind.SamsungPitCandidate;
        if (extension.Equals(".tar", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".zip", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ofp", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ops", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".kdz", StringComparison.OrdinalIgnoreCase))
            return AssetKind.FirmwareArchiveCandidate;
        if (extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            return relativePath.Contains("TESTPOINTS", StringComparison.OrdinalIgnoreCase)
                ? AssetKind.TestPointImage
                : AssetKind.Image;
        }
        if (extension.Equals(".bin", StringComparison.OrdinalIgnoreCase))
            return AssetKind.BinaryUnknown;

        return AssetKind.Unknown;
    }

    private static async Task<AssetKind> ClassifyXmlAsync(
        string path,
        string relativePath,
        CancellationToken cancellationToken,
        List<AssetDiagnostic> diagnostics)
    {
        bool hasProgram = false;
        bool hasPatch = false;
        var settings = new XmlReaderSettings
        {
            Async = true,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumXmlCharacters,
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        try
        {
            await using FileStream stream = File.OpenRead(path);
            using XmlReader reader = XmlReader.Create(stream, settings);
            int inspected = 0;
            while (await reader.ReadAsync())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                hasProgram |= reader.LocalName.Equals("program", StringComparison.OrdinalIgnoreCase);
                hasPatch |= reader.LocalName.Equals("patch", StringComparison.OrdinalIgnoreCase);
                if (++inspected > 100_000)
                    throw new InvalidDataException("XML element limit exceeded.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new AssetDiagnostic(
                "warning",
                "XmlNotClassified",
                relativePath,
                $"XML was not classified; parsing is non-networked and DTDs are prohibited. {ex.Message}"));
            return AssetKind.Unknown;
        }

        if (hasProgram && !hasPatch)
            return AssetKind.FirehoseRawProgram;
        if (hasPatch && !hasProgram)
            return AssetKind.FirehosePatch;
        return AssetKind.Unknown;
    }

    private static async Task<bool> LooksLikeScatterAsync(string path, CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[MaximumScatterBytes];
        await using FileStream stream = File.OpenRead(path);
        int read = await stream.ReadAsync(buffer, cancellationToken);
        string text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        return text.Contains("partition_index:", StringComparison.OrdinalIgnoreCase) &&
               text.Contains("linear_start_addr:", StringComparison.OrdinalIgnoreCase) &&
               text.Contains("platform:", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<AssetMetadata?> TryReadSidecarAsync(
        string root,
        string sidecarPath,
        CancellationToken cancellationToken,
        List<AssetDiagnostic> diagnostics)
    {
        string relativePath = ToRelativePath(root, sidecarPath);
        try
        {
            if ((File.GetAttributes(sidecarPath) & FileAttributes.ReparsePoint) != 0)
            {
                diagnostics.Add(new AssetDiagnostic(
                    "warning", "ReparsePointSkipped", relativePath, "Symbolic link or reparse point sidecar was not read."));
                return null;
            }

            await using FileStream stream = File.OpenRead(sidecarPath);
            using JsonDocument document = await JsonDocument.ParseAsync(
                stream, cancellationToken: cancellationToken);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("schemaVersion", out JsonElement version) ||
                !version.TryGetInt32(out int schemaVersion) ||
                schemaVersion != 1)
            {
                throw new InvalidDataException("Sidecar requires schemaVersion 1.");
            }

            AssetMetadata? metadata = document.RootElement.Deserialize<AssetMetadata>(JsonOptions);
            if (metadata is null)
                throw new InvalidDataException("Sidecar is empty.");
            if (metadata.VerificationStatus == VerificationStatus.Verified &&
                string.IsNullOrWhiteSpace(metadata.Provenance))
            {
                throw new InvalidDataException("Verified sidecar metadata requires provenance.");
            }

            return metadata;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new AssetDiagnostic("warning", "InvalidSidecar", relativePath, $"Sidecar was ignored ({ex.GetType().Name})."));
            return null;
        }
    }

    private static AssetEntry ApplyMetadata(
        AssetEntry entry,
        AssetMetadata metadata,
        string source,
        string relativePath,
        List<AssetDiagnostic> diagnostics)
    {
        AddConflictIfDifferent(nameof(metadata.Kind), entry.Kind, metadata.Kind, diagnostics, relativePath);
        AddConflictIfDifferent(nameof(metadata.Brand), entry.Brand, metadata.Brand, diagnostics, relativePath);
        AddConflictIfDifferent(nameof(metadata.Soc), entry.Soc, metadata.Soc, diagnostics, relativePath);
        AddConflictIfDifferent(nameof(metadata.ModelCodes), string.Join(',', entry.ModelCodes), metadata.ModelCodes is null ? null : string.Join(',', metadata.ModelCodes), diagnostics, relativePath);
        AddConflictIfDifferent(nameof(metadata.BoardRevision), entry.BoardRevision, metadata.BoardRevision, diagnostics, relativePath);
        AddConflictIfDifferent(nameof(metadata.BundleId), entry.BundleId, metadata.BundleId, diagnostics, relativePath);

        return entry with
        {
            Kind = metadata.Kind ?? entry.Kind,
            Brand = metadata.Brand ?? entry.Brand,
            Soc = metadata.Soc ?? entry.Soc,
            ModelCodes = metadata.ModelCodes ?? entry.ModelCodes,
            BoardRevision = metadata.BoardRevision ?? entry.BoardRevision,
            ProtocolFamily = metadata.ProtocolFamily ?? entry.ProtocolFamily,
            BitConstraint = metadata.BitConstraint ?? entry.BitConstraint,
            AndroidConstraint = metadata.AndroidConstraint ?? entry.AndroidConstraint,
            HyperOsConstraint = metadata.HyperOsConstraint ?? entry.HyperOsConstraint,
            StorageConstraint = metadata.StorageConstraint ?? entry.StorageConstraint,
            BundleId = metadata.BundleId ?? entry.BundleId,
            BundleRole = metadata.BundleRole ?? entry.BundleRole,
            Provenance = metadata.Provenance ?? entry.Provenance,
            VerificationStatus = metadata.VerificationStatus ?? entry.VerificationStatus,
            MetadataSource = source
        };
    }

    private static void AddConflictIfDifferent(
        string field,
        object? inferred,
        object? supplied,
        List<AssetDiagnostic> diagnostics,
        string relativePath)
    {
        if (inferred is null || supplied is null || Equals(inferred, supplied))
            return;
        diagnostics.Add(new AssetDiagnostic(
            "warning", "MetadataConflict", relativePath, $"Metadata field '{field}' overrides a different inferred value."));
    }

    private static AssetsManifest CreateManifest(
        string[] sourceRoots,
        AssetEntry[] entries,
        List<AssetDiagnostic> diagnostics)
    {
        AssetBundle[] bundles = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.BundleId))
            .GroupBy(entry => entry.BundleId!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new AssetBundle(
                group.Key,
                group.OrderBy(entry => entry.BundleRole, StringComparer.Ordinal)
                    .ThenBy(entry => entry.RelativePath, StringComparer.Ordinal)
                    .ToArray()))
            .ToArray();

        AssetDuplicateGroup[] duplicates = entries
            .GroupBy(entry => entry.Sha256, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new AssetDuplicateGroup(
                group.Key,
                group.Select(entry => entry.RelativePath).OrderBy(path => path, StringComparer.Ordinal).ToArray()))
            .ToArray();

        return new AssetsManifest
        {
            GeneratedAt = DateTimeOffset.UtcNow,
            SourceRoots = sourceRoots,
            Assets = entries,
            Bundles = bundles,
            DuplicateGroups = duplicates,
            UnifiedPreloadersDb = entries.Where(entry => entry.Kind == AssetKind.PreloaderCandidate).ToArray(),
            Diagnostics = diagnostics
                .OrderBy(item => item.RelativePath, StringComparer.Ordinal)
                .ThenBy(item => item.Code, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static async Task WriteAtomicallyAsync(
        string outputPath,
        AssetsManifest manifest,
        CancellationToken cancellationToken)
    {
        string temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (FileStream stream = new(
                temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(stream, manifest, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            AssetsManifest? parsed;
            await using (FileStream stream = File.OpenRead(temporaryPath))
            {
                parsed = await JsonSerializer.DeserializeAsync<AssetsManifest>(stream, JsonOptions, cancellationToken);
            }

            if (parsed is null)
                throw new InvalidDataException("Generated manifest could not be read back.");
            ValidateManifest(parsed);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static void ValidateManifest(AssetsManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
            throw new InvalidDataException($"Unsupported manifest schema version: {manifest.SchemaVersion}");
        if (manifest.GeneratedAt == default ||
            manifest.SourceRoots is null ||
            manifest.Assets is null ||
            manifest.Bundles is null ||
            manifest.DuplicateGroups is null ||
            manifest.UnifiedPreloadersDb is null ||
            manifest.Diagnostics is null ||
            manifest.SourceRoots.Length == 0 ||
            manifest.SourceRoots.Any(root => !IsValidRootId(root)) ||
            manifest.SourceRoots.Distinct(StringComparer.Ordinal).Count() != manifest.SourceRoots.Length)
        {
            throw new InvalidDataException("Manifest source roots are missing or invalid.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(PathComparer);
        var rootIds = new HashSet<string>(manifest.SourceRoots, StringComparer.Ordinal);
        var entriesByPath = new Dictionary<string, AssetEntry>(PathComparer);
        foreach (AssetEntry entry in manifest.Assets)
        {
            if (entry is null || entry.ModelCodes is null)
                throw new InvalidDataException("Manifest contains a null asset or collection.");
            string normalizedPath = NormalizeRelativePath(entry.RelativePath);
            int separator = normalizedPath.IndexOf('/');
            if (separator <= 0 ||
                !IsValidRootId(entry.RootId) ||
                !rootIds.Contains(entry.RootId) ||
                !normalizedPath[..separator].Equals(entry.RootId, StringComparison.Ordinal) ||
                !PathComparer.Equals(normalizedPath, entry.RelativePath) ||
                string.IsNullOrWhiteSpace(entry.Id) ||
                !ids.Add(entry.Id) ||
                !paths.Add(entry.RelativePath) ||
                entry.Length < 0 ||
                entry.LastWriteTimeUtc == default ||
                !Enum.IsDefined(entry.Kind) ||
                !Enum.IsDefined(entry.VerificationStatus) ||
                entry.MetadataSource is not ("content" or "sidecar" or "override") ||
                entry.Sha256.Length != 64 ||
                entry.Sha256.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidDataException($"Invalid or duplicate asset entry: {entry.RelativePath}");
            }
            entriesByPath.Add(entry.RelativePath, entry);
        }

        foreach (AssetBundle bundle in manifest.Bundles)
        {
            if (bundle is null || string.IsNullOrWhiteSpace(bundle.Id) || bundle.Members is null ||
                bundle.Members.Length == 0 ||
                bundle.Members.Any(member =>
                    member is null ||
                    !entriesByPath.TryGetValue(member.RelativePath, out AssetEntry? asset) ||
                    asset.Id != member.Id ||
                    !string.Equals(asset.BundleId, bundle.Id, StringComparison.Ordinal)))
            {
                throw new InvalidDataException("Manifest contains an invalid explicit asset bundle.");
            }
        }

        foreach (AssetDuplicateGroup group in manifest.DuplicateGroups)
        {
            if (group is null ||
                group.Sha256.Length != 64 ||
                group.Sha256.Any(character => !Uri.IsHexDigit(character)) ||
                group.RelativePaths is null ||
                group.RelativePaths.Length < 2 ||
                group.RelativePaths.Any(path =>
                    !entriesByPath.TryGetValue(path, out AssetEntry? asset) ||
                    !asset.Sha256.Equals(group.Sha256, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException("Manifest contains an invalid duplicate group.");
            }
        }

        if (manifest.UnifiedPreloadersDb.Any(entry =>
                entry is null ||
                !entriesByPath.TryGetValue(entry.RelativePath, out AssetEntry? source) ||
                source.Kind != AssetKind.PreloaderCandidate))
        {
            throw new InvalidDataException("Manifest contains an invalid unified preloader entry.");
        }

        if (manifest.Diagnostics.Any(diagnostic =>
                diagnostic is null ||
                diagnostic.Severity is not ("info" or "warning" or "error") ||
                string.IsNullOrWhiteSpace(diagnostic.Code) ||
                string.IsNullOrWhiteSpace(diagnostic.Message)))
        {
            throw new InvalidDataException("Manifest contains an invalid diagnostic.");
        }
    }

    private static bool HasNonEmptyString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString());

    private static bool HasArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) &&
        value.ValueKind == JsonValueKind.Array;

    private static bool TryReadStatus(JsonElement element, string name, out VerificationStatus status)
    {
        status = default;
        return element.TryGetProperty(name, out JsonElement value) &&
               value.ValueKind == JsonValueKind.String &&
               Enum.TryParse(value.GetString(), ignoreCase: false, out status) &&
               Enum.IsDefined(status);
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        string fullPath = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative) ||
            relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new InvalidDataException($"Path escapes assets root: {relativePath}");
        }
        return fullPath;
    }

    private static string ToRelativePath(string root, string path)
    {
        string relativePath = Path.GetRelativePath(root, Path.GetFullPath(path));
        if (Path.IsPathRooted(relativePath) ||
            relativePath == ".." ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, PathComparison))
        {
            throw new InvalidDataException($"Path escapes assets root: {path}");
        }
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string ToManifestPath(string rootId, string root, string path) =>
        rootId + "/" + ToRelativePath(root, path);

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path))
            throw new InvalidDataException("Asset paths must be non-empty and relative.");
        string normalized = path.Replace('\\', '/');
        string[] segments = normalized.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or "..") ||
            normalized.Any(char.IsControl))
        {
            throw new InvalidDataException($"Invalid relative asset path: {path}");
        }
        return normalized;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
        return options;
    }

    private static bool IsFileSystemException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static bool IsValidRootId(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static StringComparer PathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison =>
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
