using System.Text.Json;
using TensaZangetsu.Mtk.Core;
using TensaZangetsu.Mtk.Infrastructure;

if (args.Length > 0 && args[0].Equals("catalog", StringComparison.OrdinalIgnoreCase))
{
    string manifest = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../Catalog/mtk-assets.json"));
    if (!File.Exists(manifest))
    {
        Console.Error.WriteLine($"Catalog manifest not found: {manifest}");
        return 1;
    }

    using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(manifest));
    if (args.Skip(1).Any(static argument => argument.Equals("--json", StringComparison.OrdinalIgnoreCase)))
    {
        Console.WriteLine(await File.ReadAllTextAsync(manifest));
    }
    else if (document.RootElement.TryGetProperty("models", out JsonElement models))
    {
        foreach (JsonElement model in models.EnumerateArray())
        {
            string id = model.GetProperty("id").GetString() ?? "unknown";
            int assets = model.TryGetProperty("assets", out JsonElement items) ? items.GetArrayLength() : 0;
            Console.WriteLine($"{id}: {assets} assets");
        }
    }

    return 0;
}

if (args.Length == 0 || !args[0].Equals("identify", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: tensa-mtk identify [--json] | catalog [--json]");
    return 2;
}

bool json = args.Skip(1).Any(static argument => argument.Equals("--json", StringComparison.OrdinalIgnoreCase));
using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

var service = new MtkDeviceService();
var progress = new Progress<MtkLogEntry>(entry =>
{
    if (!json)
        Console.Error.WriteLine($"[{entry.Timestamp:O}] {entry.Level}: {entry.Message}");
});

MtkOperationResult result = await service.IdentifyAsync(progress, cancellation.Token);
if (json)
{
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
}
else if (result.Device is not null)
{
    Console.WriteLine($"Chip: {result.Device.ChipName} ({result.Device.HardwareCode})");
    Console.WriteLine($"Stage: {result.Device.BootStage}");
    Console.WriteLine($"Security: SecureBoot={result.Device.SecureBoot}, SLA={result.Device.Sla}, DAA={result.Device.Daa}");
}

if (!result.Success)
    Console.Error.WriteLine(result.Message);

return result.Success ? 0 : 1;
