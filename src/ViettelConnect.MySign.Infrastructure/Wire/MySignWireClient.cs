using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Application.Signing;
using ViettelConnect.MySign.Domain.Certificates;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Domain.Signing;
using ViettelConnect.MySign.Infrastructure.Configuration;
using ViettelConnect.MySign.Infrastructure.Wire.Mapping;

namespace ViettelConnect.MySign.Infrastructure.Wire;

/// <summary>
/// Client mức dây tới Viettel MySign. Mỗi lời gọi nhận <see cref="MySignCredentials"/> riêng (theo người ký và
/// đơn vị) và gắn chúng vào <see cref="HttpRequestMessage.Options"/> để handler xác thực chọn đúng token;
/// request login được đánh dấu bỏ qua xác thực.
/// </summary>
internal sealed class MySignWireClient : IMySignWireClient
{
    internal const string HashAlgoOid = "2.16.840.1.101.3.4.2.1";
    internal const string SignAlgoOid = "1.2.840.113549.1.1.1";

    private readonly HttpClient _http;
    private readonly Uri _baseUri;
    private readonly bool _includeRaw;
    private readonly ICorrelationIdAccessor _correlation;
    private readonly TimeProvider _time;

    public MySignWireClient(
        HttpClient http,
        IOptions<MySignOptions> options,
        ICorrelationIdAccessor correlation,
        TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(options);

        var value = options.Value;
        _http = http;
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _includeRaw = value.Errors.IncludeRawErrorMessage;

        // Route tương đối chỉ phân giải đúng dưới /vtss/service/ khi base có dấu "/" cuối.
        var raw = http.BaseAddress?.ToString() ?? value.BaseUrl;
        _baseUri = new Uri(raw.EndsWith('/') ? raw : raw + "/", UriKind.Absolute);
        http.BaseAddress ??= _baseUri;
    }

    /// <summary>Mã tương quan của lời gọi hiện tại (ưu tiên mã đang mượn khi handler xác thực tự đăng nhập).</summary>
    private string CorrelationId => AmbientCorrelation.Resolve(_correlation);

    public async Task<AccessToken> LoginAsync(MySignCredentials c, CancellationToken ct)
    {
        const string op = "login";
        var body = new LoginRequestDto
        {
            ClientId = c.ClientId,
            ClientSecret = c.ClientSecret,
            ProfileId = c.ProfileId,
            UserId = c.UserId,
        };

        var dto = await PostAsync<LoginRequestDto, LoginResponseDto>(MySignRoutes.Login, op, c, body, skipAuth: true, noRetry: false, ct);
        if (string.IsNullOrWhiteSpace(dto.AccessToken))
        {
            throw ErrorMapper.Protocol(op, "access_token missing", CorrelationId);
        }

        if (!long.TryParse(dto.ExpiresIn, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
        {
            throw ErrorMapper.Protocol(op, "expires_in missing or invalid", CorrelationId);
        }

        return new AccessToken(dto.AccessToken, _time.GetUtcNow().AddSeconds(seconds));
    }

    public async Task<IReadOnlyList<Certificate>> GetCertificatesAsync(MySignCredentials c, CancellationToken ct)
    {
        const string op = "certificates/info";
        var body = new CertificateInfoRequestDto
        {
            ClientId = c.ClientId,
            ClientSecret = c.ClientSecret,
            ProfileId = c.ProfileId,
            UserId = c.UserId,
        };

        var dtos = await PostAsync<CertificateInfoRequestDto, List<CertificateInfoDto>>(
            MySignRoutes.CertificatesInfo, op, c, body, skipAuth: false, noRetry: false, ct);

        try
        {
            return dtos.Select(CertificateMapper.ToDomain).ToArray();
        }
        catch (FormatException ex)
        {
            throw ErrorMapper.Protocol(op, "certificate entry invalid", CorrelationId, ex);
        }
    }

    public async Task<SignHashWireResponse> SignHashAsync(MySignCredentials c, SignHashWireRequest r, CancellationToken ct)
    {
        const string op = "signHash";
        var body = new SignHashRequestDto
        {
            CredentialId = r.CredentialId,
            ClientId = c.ClientId,
            ClientSecret = c.ClientSecret,
            NumSignatures = r.Documents.Count,
            Description = r.EncodedDescription,
            Documents = r.Documents
                .Select(d => new SignHashDocumentDto { DocumentId = d.DocumentId, DocumentName = d.EncodedName })
                .ToList(),
            Hash = r.Documents.Select(d => Convert.ToBase64String(d.Sha256)).ToList(),
            HashAlgo = HashAlgoOid,
            SignAlgo = SignAlgoOid,
            Async = r.AsyncCode,
            Sad = r.Sad,
        };

        var dto = await PostAsync<SignHashRequestDto, SignHashResponseDto>(MySignRoutes.SignHash, op, c, body, skipAuth: false, noRetry: true, ct);
        if (dto.Signatures is null && string.IsNullOrWhiteSpace(dto.TransactionId))
        {
            throw ErrorMapper.Protocol(op, "neither signatures nor transactionId", CorrelationId);
        }

        var signatures = dto.Signatures is null ? null : DecodeSignatures(dto.Signatures, op);
        var tx = string.IsNullOrWhiteSpace(dto.TransactionId) ? null : dto.TransactionId;
        return new SignHashWireResponse(tx, signatures);
    }

    public async Task<StatusSnapshot> GetStatusAsync(MySignCredentials c, string transactionId, CancellationToken ct)
    {
        const string op = "requests/status";
        var body = new RequestStatusRequestDto { TransactionId = transactionId };

        var dto = await PostAsync<RequestStatusRequestDto, RequestStatusResponseDto>(
            MySignRoutes.RequestStatus, op, c, body, skipAuth: false, noRetry: false, ct);

        var raw = string.IsNullOrWhiteSpace(dto.Status) ? null : dto.Status.Trim();
        var signatures = dto.Signatures is null ? [] : DecodeSignatures(dto.Signatures, op);
        return new StatusSnapshot(
            raw,
            SignStatusCodes.Classify(raw),
            signatures,
            dto.ErrorCode,
            _includeRaw ? dto.ErrorDescription : null);
    }

    private IReadOnlyList<byte[]> DecodeSignatures(List<string> encoded, string op)
    {
        try
        {
            return encoded.Select(Base64.Decode).ToArray();
        }
        catch (FormatException ex)
        {
            throw ErrorMapper.Protocol(op, "signature is not valid base64", CorrelationId, ex);
        }
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string route,
        string op,
        MySignCredentials credentials,
        TRequest body,
        bool skipAuth,
        bool noRetry,
        CancellationToken ct)
        where TResponse : class
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_baseUri, route))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, MySignJsonOptions.Wire),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Options.Set(MySignRequestOptions.Credentials, credentials);
        request.Options.Set(MySignRequestOptions.SkipAuth, skipAuth);
        request.Options.Set(MySignRequestOptions.NoRetry, noRetry);
        request.Options.Set(MySignRequestOptions.CorrelationId, CorrelationId);

        HttpResponseMessage response;
        string text;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            using (response)
            {
                text = await response.Content.ReadAsStringAsync(ct);
                if (!response.IsSuccessStatusCode)
                {
                    throw ErrorMapper.FromHttpError(
                        op,
                        (int)response.StatusCode,
                        TryParse<ErrorDto>(text),
                        text,
                        _includeRaw,
                        credentials.ClientSecret,
                        CorrelationId);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            throw ErrorMapper.Transport(op, ex, CorrelationId);
        }
        catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
        {
            // Hết hạn HttpClient.Timeout (không phải người gọi huỷ).
            throw ErrorMapper.Transport(op, ex, CorrelationId);
        }

        var result = TryParse<TResponse>(text);
        return result ?? throw ErrorMapper.Protocol(op, "body is not the expected JSON shape", CorrelationId);
    }

    private static T? TryParse<T>(string text)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, MySignJsonOptions.Wire);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
