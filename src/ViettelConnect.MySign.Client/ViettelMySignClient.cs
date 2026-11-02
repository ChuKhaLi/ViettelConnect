using ViettelConnect.MySign.Application.UseCases;
using ViettelConnect.MySign.Client.Dtos;
using ViettelConnect.MySign.Client.Mapping;

namespace ViettelConnect.MySign.Client;

/// <summary>Cài đặt facade: ánh xạ DTO công khai sang use case tầng Application và ngược lại.</summary>
internal sealed class ViettelMySignClient : IViettelMySignClient
{
    private readonly ListCertificates _listCertificates;
    private readonly SignHashes _signHashes;
    private readonly BeginSignHashes _beginSignHashes;
    private readonly HandleCallbackTokenRequest _handleCallbackToken;
    private readonly HandleCallbackResult _handleCallbackResult;

    public ViettelMySignClient(
        ListCertificates listCertificates,
        SignHashes signHashes,
        BeginSignHashes beginSignHashes,
        HandleCallbackTokenRequest handleCallbackToken,
        HandleCallbackResult handleCallbackResult)
    {
        _listCertificates = listCertificates ?? throw new ArgumentNullException(nameof(listCertificates));
        _signHashes = signHashes ?? throw new ArgumentNullException(nameof(signHashes));
        _beginSignHashes = beginSignHashes ?? throw new ArgumentNullException(nameof(beginSignHashes));
        _handleCallbackToken = handleCallbackToken ?? throw new ArgumentNullException(nameof(handleCallbackToken));
        _handleCallbackResult = handleCallbackResult ?? throw new ArgumentNullException(nameof(handleCallbackResult));
    }

    public async Task<IReadOnlyList<CertificateDto>> ListCertificatesAsync(CancellationToken ct = default)
    {
        var certificates = await _listCertificates.ExecuteAsync(ct).ConfigureAwait(false);
        return DtoMapper.ToDto(certificates);
    }

    public async Task<SignHashesResultDto> SignHashesAsync(SignHashesRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await _signHashes.ExecuteAsync(DtoMapper.ToRequest(request), ct).ConfigureAwait(false);
        return DtoMapper.ToDto(result);
    }

    public async Task<BeginSignResultDto> BeginSignHashesAsync(
        SignHashesRequestDto request,
        string tenantKey = "default",
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await _beginSignHashes.ExecuteAsync(DtoMapper.ToRequest(request), tenantKey, ct).ConfigureAwait(false);
        return DtoMapper.ToDto(result);
    }

    public async Task<CallbackResponseDto> HandleCallbackTokenRequestAsync(string requestJson, CancellationToken ct = default)
    {
        var response = await _handleCallbackToken.ExecuteAsync(requestJson, ct).ConfigureAwait(false);
        return DtoMapper.ToDto(response);
    }

    public async Task<(CallbackResponseDto Response, CallbackSignOutcomeDto? Outcome)> HandleCallbackResultAsync(
        string? bearerToken,
        string requestJson,
        CancellationToken ct = default)
    {
        var (response, outcome) = await _handleCallbackResult.ExecuteAsync(bearerToken, requestJson, ct).ConfigureAwait(false);
        return (DtoMapper.ToDto(response), outcome is null ? null : DtoMapper.ToDto(outcome));
    }
}
