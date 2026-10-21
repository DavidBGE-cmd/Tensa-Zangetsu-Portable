using System.Text.Json;
using TensaZangetsu.AssetIndexing;

namespace TensaZangetsu.CLI;

internal static class AssetCommands
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal) || args.Contains("-h", StringComparer.Ordinal))
        {
            PrintHelp();
            return 0;
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
            return args[0].ToLowerInvariant() switch
            {
                "index" => await IndexAsync(args[1..], cancellation.Token),
                "validate" => await ValidateAsync(args[1..], cancellation.Token),
                _ => InvalidCommand(args[0])
            };
        }
        catch (OperationCanceledException)
        {
            bool json = args.Contains("--json", StringComparer.Ordinal);
            if (json)
                Console.Out.WriteLine(JsonSerializer.Serialize(new { type = "state", state = "Cancelled" }));
            else
                Console.Error.WriteLine("Asset indexing cancelled.");
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

    private static async Task<int> IndexAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = Parse(args, allowManifest: false);
        if (options.Roots.Count == 0)
            throw new ArgumentException("At least one --root path is required.");

        AssetIndexResult result = await new AssetIndexingService().IndexAsync(
            new AssetIndexOptions(
                options.Roots.ToArray(),
                options.CatalogPath ?? Path.Combine("Catalog", "devices.json"),
                options.OverridesPath ?? Path.Combine("Catalog", "overrides.json"),
                options.OutputPath ?? Path.Combine("Catalog", "AssetsManifest.json"),
                options.Rehash),
            new ConsoleProgress(options.Json),
            cancellationToken);

        WriteEvent(new
        {
            type = "result",
            status = result.FilesFailed == 0 ? "Completed" : "CompletedWithErrors",
            result.ManifestPath,
            result.FilesIndexed,
            result.FilesFailed,
            result.DuplicateGroups,
            result.Bundles,
            result.Diagnostics
        }, options.Json);
        return result.FilesFailed == 0 ? 0 : 3;
    }

    private static async Task<int> ValidateAsync(string[] args, CancellationToken cancellationToken)
    {
        var options = Parse(args, allowManifest: true);
        if (string.IsNullOrWhiteSpace(options.ManifestPath))
            throw new ArgumentException("assets validate requires --manifest <path>.");
        if (options.Roots.Count != 0)
            throw new ArgumentException("assets validate does not accept --root.");
        AssetsManifest manifest = await AssetIndexingService.ReadAndValidateManifestAsync(
            options.ManifestPath, cancellationToken);
        WriteEvent(new { type = "result", status = "Valid", manifest.SchemaVersion, assetCount = manifest.Assets.Length }, options.Json);
        return 0;
    }

    private static CommandOptions Parse(string[] args, bool allowManifest)
    {
        var options = new CommandOptions();
        for (int index = 0; index < args.Length; index++)
        {
            string name = args[index];
            string? inlineValue = null;
            int equals = name.IndexOf('=');
            if (equals >= 0)
            {
                inlineValue = name[(equals + 1)..];
                name = name[..equals];
            }

            switch (name)
            {
                case "--root":
                    string rootValue = ReadValue(args, ref index, name, inlineValue);
                    int separator = rootValue.IndexOf('=');
                    if (separator > 0 && separator < rootValue.Length - 1)
                        options.Roots.Add(new AssetIndexRoot(rootValue[..separator], rootValue[(separator + 1)..]));
                    else
                        options.Roots.Add(new AssetIndexRoot(
                            options.Roots.Count == 0 ? "assets" : $"assets-{options.Roots.Count + 1}",
                            rootValue));
                    break;
                case "--catalog":
                    options.CatalogPath = ReadValue(args, ref index, name, inlineValue);
                    break;
                case "--overrides":
                    options.OverridesPath = ReadValue(args, ref index, name, inlineValue);
                    break;
                case "--output":
                    options.OutputPath = ReadValue(args, ref index, name, inlineValue);
                    break;
                case "--manifest" when allowManifest:
                    options.ManifestPath = ReadValue(args, ref index, name, inlineValue);
                    break;
                case "--rehash":
                    options.Rehash = true;
                    break;
                case "--json":
                    options.Json = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown assets option: {name}");
            }
        }

        return options;
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

    private static int InvalidCommand(string command)
    {
        Console.Error.WriteLine($"Unknown assets command: {command}");
        PrintHelp();
        return 2;
    }

    private static void WriteEvent<T>(T value, bool asJson)
    {
        string json = JsonSerializer.Serialize(value);
        if (asJson)
        {
            Console.Out.WriteLine(json);
            return;
        }

        using JsonDocument document = JsonDocument.Parse(json);
        foreach (JsonProperty property in document.RootElement.EnumerateObject())
            Console.Out.WriteLine($"{property.Name}: {property.Value}");
    }

    private static void PrintHelp()
    {
        Console.Out.WriteLine("""
            tensa assets index --root <path|id=path> [--root <path|id=path>] [--catalog <path>] [--overrides <path>] [--output <path>] [--rehash] [--json]
            tensa assets validate --manifest <path> [--json]

            Indexing is read-only. SHA-256 is streamed; symbolic links are skipped.
            Repeated --root options support the repository's separate local asset folders.
            --rehash recomputes all hashes instead of reusing entries with matching size and timestamp.
            Exit codes: 0 success, 1 index/validation failure, 2 invalid arguments,
            3 partial inventory with file errors, 4 cancelled.
            """);
    }

    private sealed class CommandOptions
    {
        public List<AssetIndexRoot> Roots { get; } = [];
        public string? CatalogPath { get; set; }
        public string? OverridesPath { get; set; }
        public string? OutputPath { get; set; }
        public string? ManifestPath { get; set; }
        public bool Rehash { get; set; }
        public bool Json { get; set; }
    }

    private sealed class ConsoleProgress(bool asJson) : IProgress<AssetIndexProgress>
    {
        public void Report(AssetIndexProgress value)
        {
            if (value.FilesProcessed % 250 != 0)
                return;

            if (asJson)
            {
                Console.Out.WriteLine(JsonSerializer.Serialize(new
                {
                    type = "progress",
                    value.FilesProcessed,
                    value.RelativePath
                }));
            }
            else
            {
                Console.Error.WriteLine($"Indexed {value.FilesProcessed}: {value.RelativePath}");
            }
        }
    }
}
