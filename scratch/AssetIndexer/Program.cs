using System.Text.Json;
using TensaZangetsu.AssetIndexing;

return await AssetIndexerCli.RunAsync(args);

internal static class AssetIndexerCli
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            PrintHelp();
            return 0;
        }

        CliArguments parsed;
        try
        {
            parsed = Parse(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            PrintHelp();
            return 2;
        }

        if (parsed.ManifestPath is not null && parsed.Roots.Count > 0)
        {
            Console.Error.WriteLine("--manifest cannot be combined with indexing roots.");
            return 2;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;

        try
        {
            if (parsed.ManifestPath is not null && parsed.Roots.Count == 0)
            {
                AssetsManifest manifest = await AssetIndexingService.ReadAndValidateManifestAsync(
                    parsed.ManifestPath, cancellation.Token);
                WriteResult(new
                {
                    valid = true,
                    manifest.SchemaVersion,
                    assetCount = manifest.Assets.Length,
                    diagnostics = manifest.Diagnostics.Length
                }, parsed.Json);
                return 0;
            }

            if (parsed.Roots.Count == 0)
                throw new ArgumentException("At least one --assets root is required.");

            string catalogPath = parsed.CatalogPath ?? Path.Combine("Catalog", "devices.json");
            string overridesPath = parsed.OverridesPath ?? Path.Combine("Catalog", "overrides.json");
            string outputPath = parsed.OutputPath ?? Path.Combine("Catalog", "AssetsManifest.json");
            var options = new AssetIndexOptions(
                parsed.Roots.ToArray(),
                catalogPath,
                overridesPath,
                outputPath,
                parsed.Rehash);

            var progress = new ConsoleProgress(parsed.Json);
            AssetIndexResult result = await new AssetIndexingService().IndexAsync(
                options, progress, cancellation.Token);

            if (parsed.Validate)
            {
                AssetsManifest manifest = await AssetIndexingService.ReadAndValidateManifestAsync(
                    result.ManifestPath, cancellation.Token);
                WriteResult(new
                {
                    valid = true,
                    result.ManifestPath,
                    result.FilesIndexed,
                    result.FilesFailed,
                    result.DuplicateGroups,
                    result.Bundles,
                    result.Diagnostics,
                    assetCount = manifest.Assets.Length
                }, parsed.Json);
            }
            else
            {
                WriteResult(result, parsed.Json);
            }

            return result.FilesFailed == 0 ? 0 : 3;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Indexing cancelled.");
            return 4;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= handler;
        }
    }

    private static CliArguments Parse(string[] args)
    {
        var parsed = new CliArguments();
        for (int index = 0; index < args.Length; index++)
        {
            string name = args[index];
            string? inlineValue = null;
            int equalsIndex = name.IndexOf('=');
            if (equalsIndex >= 0)
            {
                inlineValue = name[(equalsIndex + 1)..];
                name = name[..equalsIndex];
            }

            switch (name)
            {
                case "--assets":
                case "--assets-root":
                    string value = ReadValue(args, ref index, name, inlineValue);
                    int rootEquals = value.IndexOf('=');
                    string rootId;
                    string rootPath;
                    if (rootEquals > 0 && rootEquals < value.Length - 1)
                    {
                        rootId = value[..rootEquals];
                        rootPath = value[(rootEquals + 1)..];
                    }
                    else
                    {
                        rootId = parsed.Roots.Count == 0 ? "assets" : $"assets-{parsed.Roots.Count + 1}";
                        rootPath = value;
                    }
                    parsed.Roots.Add(new AssetIndexRoot(rootId, rootPath));
                    break;
                case "--catalog":
                    parsed.CatalogPath = ReadValue(args, ref index, name, inlineValue);
                    break;
                case "--overrides":
                    parsed.OverridesPath = ReadValue(args, ref index, name, inlineValue);
                    break;
                case "--output":
                    parsed.OutputPath = ReadValue(args, ref index, name, inlineValue);
                    break;
                case "--manifest":
                    parsed.ManifestPath = ReadValue(args, ref index, name, inlineValue);
                    break;
                case "--validate":
                    parsed.Validate = true;
                    break;
                case "--rehash":
                    parsed.Rehash = true;
                    break;
                case "--json":
                    parsed.Json = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {name}");
            }
        }

        return parsed;
    }

    private static string ReadValue(string[] args, ref int index, string option, string? inlineValue)
    {
        string? value = inlineValue;
        if (value is null && index + 1 < args.Length && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            value = args[++index];
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Missing value for {option}.");
        return value;
    }

    private static void WriteResult<T>(T result, bool asJson)
    {
        if (asJson)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(result));
            return;
        }

        foreach (var property in typeof(T).GetProperties())
            Console.Out.WriteLine($"{property.Name}: {property.GetValue(result)}");
    }

    private static void PrintHelp()
    {
        Console.Out.WriteLine("""
            AssetIndexer

            Index files (all roots are read-only):
              dotnet run --project scratch/AssetIndexer -- --assets <path> --catalog Catalog/devices.json --overrides Catalog/overrides.json --output Catalog/AssetsManifest.json
              dotnet run --project scratch/AssetIndexer -- --assets external=ExternalAssets --assets testpoints=TESTPOINTS --assets app=src/TensaZangetsu.App/Assets --assets services=src/TensaZangetsu.Services/Assets --catalog Catalog/devices.json --overrides Catalog/overrides.json --output Catalog/AssetsManifest.json --validate

            Validate a manifest without indexing:
              dotnet run --project scratch/AssetIndexer -- --manifest Catalog/AssetsManifest.json --validate

            Options:
              --assets <path|id=path>  Add a root; repeat for multiple roots
              --catalog <path>         Device catalog JSON (default: Catalog/devices.json)
              --overrides <path>       Explicit metadata overrides (default: Catalog/overrides.json)
              --output <path>          Manifest destination (default: Catalog/AssetsManifest.json)
              --manifest <path>        Validate an existing manifest
              --validate               Validate generated output before reporting success
              --rehash                 Recompute every SHA-256 instead of reusing unchanged entries
              --json                   Emit one JSON result line; progress is sent to stderr
              --help                   Show this help

            Exit codes: 0 success, 1 index/validation failure, 2 invalid arguments,
            3 partial inventory with file errors, 4 cancelled.
            """);
    }

    private sealed class CliArguments
    {
        public List<AssetIndexRoot> Roots { get; } = [];
        public string? CatalogPath { get; set; }
        public string? OverridesPath { get; set; }
        public string? OutputPath { get; set; }
        public string? ManifestPath { get; set; }
        public bool Validate { get; set; }
        public bool Rehash { get; set; }
        public bool Json { get; set; }
    }

    private sealed class ConsoleProgress(bool json) : IProgress<AssetIndexProgress>
    {
        public void Report(AssetIndexProgress value)
        {
            if (value.FilesProcessed % 250 != 0)
                return;
            string message = $"Indexed {value.FilesProcessed}: {value.RelativePath}";
            if (json)
                Console.Error.WriteLine(message);
            else
                Console.Out.WriteLine(message);
        }
    }
}
