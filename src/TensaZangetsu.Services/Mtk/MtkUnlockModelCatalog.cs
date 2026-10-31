using System.Text.Json;

namespace TensaZangetsu.Services.Mtk;

/// <summary>Read-only metadata index imported from an external model database.</summary>
public sealed class MtkUnlockModelCatalog
{
    private readonly IReadOnlyList<MtkUnlockModel> _models;

    public MtkUnlockModelCatalog(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (document.RootElement.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Unsupported model catalog schema.");

        var models = new List<MtkUnlockModel>();
        foreach (JsonElement element in document.RootElement.GetProperty("models").EnumerateArray())
        {
            models.Add(new(
                element.GetProperty("id").GetString() ?? throw new InvalidDataException("Model id"),
                Text(element, "brand"),
                Text(element, "model") ?? throw new InvalidDataException("Model name"),
                Text(element, "deviceName"),
                Text(element, "chipset"),
                Text(element, "platform"),
                Text(element, "mode"),
                Text(element, "connection"),
                Text(element, "loaderName"),
                Text(element, "preloaderName"),
                element.GetProperty("profilePath").GetString() ?? throw new InvalidDataException("Profile path"),
                element.GetProperty("profileSha256").GetString() ?? throw new InvalidDataException("Profile hash"),
                element.GetProperty("verificationStatus").GetString() ?? throw new InvalidDataException("Verification status")));
        }
        _models = models.AsReadOnly();
    }

    public IReadOnlyList<MtkUnlockModel> Models => _models;

    public IReadOnlyList<MtkUnlockModel> Find(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return _models.Where(model =>
            model.Model.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            model.DeviceName?.Contains(query, StringComparison.OrdinalIgnoreCase) == true ||
            model.Brand?.Contains(query, StringComparison.OrdinalIgnoreCase) == true).ToArray();
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) &&
        value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
}

public sealed record MtkUnlockModel(
    string Id,
    string? Brand,
    string Model,
    string? DeviceName,
    string? Chipset,
    string? Platform,
    string? Mode,
    string? Connection,
    string? LoaderName,
    string? PreloaderName,
    string ProfilePath,
    string ProfileSha256,
    string VerificationStatus);
