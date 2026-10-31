using TensaZangetsu.Protocol.Mtk.Abstractions;

namespace TensaZangetsu.Services.Mtk;

/// <summary>Builds explicit MTK connection resources from verified catalog entries.</summary>
public sealed class MtkCatalogConnectionResources
{
    private readonly MtkCatalogResourceProvider _provider;

    public MtkCatalogConnectionResources(MtkCatalogResourceProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    public async ValueTask<MtkConnectionResources> ResolveAsync(
        MtkTargetInfo target,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        MtkDaImage da = await _provider.GetDownloadAgentAsync(target, cancellationToken)
            .ConfigureAwait(false);
        MtkEmiImage? emi = await _provider.GetEmiAsync(target, cancellationToken)
            .ConfigureAwait(false);

        MtkSensitiveBuffer? authentication = null;
        MtkSensitiveBuffer? certificate = null;
        try
        {
            if (_provider.HasVerifiedAuthentication(target, "auth"))
                authentication = _provider.OpenVerifiedAuthentication(target, "auth");
            if (_provider.HasVerifiedAuthentication(target, "certificate"))
                certificate = _provider.OpenVerifiedAuthentication(target, "certificate");
            return new(da, emi, authentication, certificate);
        }
        catch
        {
            authentication?.Dispose();
            certificate?.Dispose();
            throw;
        }
    }
}
