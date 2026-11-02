using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ViettelConnect.MySign.IntegrationTests.Fake;

/// <summary>Một request fake server đã nhận: đường dẫn, thân JSON thô và header Authorization.</summary>
internal sealed record RecordedRequest(string Path, string Body, string? Authorization)
{
    public JsonDocument Json() => JsonDocument.Parse(Body);
}

/// <summary>Chứng thư mà fake trả về trong certificates/info.</summary>
internal sealed class FakeCertificate
{
    public string CredentialId { get; set; } = "cred-001";
    public string KeyStatus { get; set; } = "ENABLED";
    public string[] KeyAlgo { get; set; } = ["1.2.840.113549.1.1.1"];
    public byte[][] Chain { get; set; } = [[1, 2, 3], [4, 5], [6]];
    public string SerialNumber { get; set; } = "5AAC41CD8FA22B953640";
    public string SubjectDn { get; set; } = "CN=Nguyen Van A,O=Cong ty ABC";
    public string IssuerDn { get; set; } = "CN=Viettel-CA";
    public string ValidFrom { get; set; } = "20240709132216+0000";
    public string ValidTo { get; set; } = "20260709132216Z";
}

/// <summary>Kịch bản cấu hình được cho fake MySign.</summary>
internal sealed class FakeMySignScenario
{
    public List<FakeCertificate> Certificates { get; } = [new FakeCertificate()];

    /// <summary>Giá trị expires_in (chuỗi giây) trả về cho login.</summary>
    public string ExpiresIn { get; set; } = "3600";

    /// <summary>Lỗi cố định theo route tương đối (vd "signHash"): HTTP status + thân thô.</summary>
    public ConcurrentDictionary<string, (int Status, string Body)> ErrorByRoute { get; } = new();

    /// <summary>Route tương đối -> HTTP status lỗi (thân rỗng) chỉ trả MỘT lần rồi trở lại bình thường.</summary>
    public ConcurrentDictionary<string, int> FailOnceByRoute { get; } = new();

    /// <summary>Route tương đối -> (HTTP status, số lần còn lại): trả lỗi thân rỗng đúng N lần rồi trở lại bình thường.</summary>
    public ConcurrentDictionary<string, (int Status, int Remaining)> FailTimesByRoute { get; } = new();

    /// <summary>Bật thì signHash đồng bộ trả thiếu chữ ký cuối (số chữ ký != số mã băm).</summary>
    public bool DropLastSignature { get; set; }

    /// <summary>signHash bất đồng bộ không trả transactionId (vi phạm giao thức).</summary>
    public bool OmitTransactionId { get; set; }

    /// <summary>Chuỗi status lần lượt cho mỗi transactionId; phần tử cuối được lặp lại.</summary>
    public ConcurrentDictionary<string, List<string>> StatusSequence { get; } = new();

    /// <summary>Chèn "\r\n" giữa chuỗi base64 của chữ ký để kiểm tra khả năng chịu khoảng trắng.</summary>
    public bool WrapSignaturesWithCrLf { get; set; }

    /// <summary>Bật thì mọi route trừ login yêu cầu Bearer do login cấp; thiếu/sai thì 401 59033.</summary>
    public bool RequireBearer { get; set; }

    /// <summary>Route tương đối -> độ trễ trước khi trả lời (mô phỏng máy chủ treo); huỷ theo request bị huỷ.</summary>
    public ConcurrentDictionary<string, TimeSpan> DelayByRoute { get; } = new();

    /// <summary>Login của client_id này chỉ trả lời sau khi task tương ứng hoàn tất (kiểm chứng tách biệt giữa đơn vị).</summary>
    public ConcurrentDictionary<string, Task> LoginGateByClient { get; } = new();
}

/// <summary>
/// Máy chủ MySign giả (WebApplication, cổng ngẫu nhiên trên loopback) cho login, certificates/info,
/// signHash, requests/status. Ghi lại mọi request để kiểm chứng.
/// </summary>
internal sealed class FakeMySignServer : IAsyncDisposable
{
    public const string ServicePrefix = "/vtss/service/";

    private readonly WebApplication _app;
    private readonly FakeMySignScenario _scenario = new();
    private readonly List<RecordedRequest> _requests = [];
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, List<string>> _hashesByTx = new();
    private readonly ConcurrentDictionary<string, int> _statusCallsByTx = new();
    private readonly ConcurrentDictionary<string, byte> _issuedTokens = new();
    private readonly ConcurrentDictionary<string, string> _lastTokenByClient = new();
    private int _loginCount;
    private int _txSeq;
    private int _tokenSeq;

    private FakeMySignServer(WebApplication app, string rootUrl)
    {
        _app = app;
        RootUrl = rootUrl;
    }

    /// <summary>http://127.0.0.1:port (không có đường dẫn).</summary>
    public string RootUrl { get; }

    /// <summary>Giá trị BaseUrl tương ứng cấu hình thật: .../vtss/service (không dấu "/" cuối).</summary>
    public string BaseUrl => RootUrl + "/vtss/service";

    public FakeMySignScenario Scenario => _scenario;

    public int LoginCount => Volatile.Read(ref _loginCount);

    /// <summary>Làm mọi token đã cấp mất hiệu lực (mô phỏng hết hạn phía máy chủ).</summary>
    public void RevokeIssuedTokens() => _issuedTokens.Clear();

    /// <summary>Token mới nhất đã cấp cho client_id (để test đối chiếu header).</summary>
    public string TokenIssuedFor(string clientId) => _lastTokenByClient[clientId];

    public IReadOnlyList<RecordedRequest> Requests
    {
        get { lock (_lock) { return _requests.ToArray(); } }
    }

    public IReadOnlyList<RecordedRequest> RequestsFor(string route) =>
        Requests.Where(r => r.Path == ServicePrefix + route).ToArray();

    /// <summary>Chữ ký giả suy ra từ một mã băm (base64) — để test tính ra giá trị mong đợi.</summary>
    public static byte[] SignatureFor(string hashBase64) => Encoding.UTF8.GetBytes("sig:" + hashBase64);

    public static async Task<FakeMySignServer> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0/");
        builder.Logging.ClearProviders();
        builder.Services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        var app = builder.Build();

        FakeMySignServer? self = null;

        void Map(string route, Func<HttpContext, string, Task> handler)
        {
            app.MapPost(ServicePrefix + route, async ctx =>
            {
                string body;
                using (var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8))
                {
                    body = await reader.ReadToEndAsync();
                }

                var auth = ctx.Request.Headers.Authorization.ToString();
                lock (self!._lock)
                {
                    self._requests.Add(new RecordedRequest(ctx.Request.Path.Value!, body, string.IsNullOrEmpty(auth) ? null : auth));
                }

                if (route == "ras/v1/login")
                {
                    Interlocked.Increment(ref self._loginCount);
                }

                if (self._scenario.DelayByRoute.TryGetValue(route, out var delay))
                {
                    try
                    {
                        await Task.Delay(delay, ctx.RequestAborted);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }

                if (self._scenario.ErrorByRoute.TryGetValue(route, out var err))
                {
                    await Write(ctx, err.Status, err.Body);
                    return;
                }

                if (self._scenario.FailOnceByRoute.TryRemove(route, out var onceStatus))
                {
                    await Write(ctx, onceStatus, "{}");
                    return;
                }

                if (self.TryConsumeFailure(route, out var timesStatus))
                {
                    await Write(ctx, timesStatus, string.Empty);
                    return;
                }

                if (self._scenario.RequireBearer && route != "ras/v1/login" && !self.BearerIsValid(auth))
                {
                    await Write(ctx, 401, "{\"error\":\"59033\",\"error_description\":\"Failed to process request - invalid token\"}");
                    return;
                }

                await handler(ctx, body);
            });
        }

        Map("ras/v1/login", async (ctx, loginBody) =>
        {
            string? clientId;
            using (var loginDoc = JsonDocument.Parse(loginBody))
            {
                clientId = loginDoc.RootElement.TryGetProperty("client_id", out var cid) ? cid.GetString() : null;
            }

            if (clientId is not null && self!._scenario.LoginGateByClient.TryGetValue(clientId, out var gate))
            {
                await gate;
            }

            var token = "fake-token-" + Interlocked.Increment(ref self!._tokenSeq);
            self._issuedTokens[token] = 0;
            if (clientId is not null)
            {
                self._lastTokenByClient[clientId] = token;
            }

            await Write(ctx, 200, JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["access_token"] = token,
                ["refresh_token"] = string.Empty,
                ["token_type"] = "Bearer",
                ["expires_in"] = self._scenario.ExpiresIn,
            }));
        });

        Map("certificates/info", async (ctx, _) =>
        {
            var array = self!._scenario.Certificates.Select(c => new Dictionary<string, object?>
            {
                ["description"] = "Go>Sign mobile based implicit credential authorization",
                ["key"] = new Dictionary<string, object?> { ["status"] = c.KeyStatus, ["algo"] = c.KeyAlgo, ["len"] = 2048, ["curve"] = null },
                ["cert"] = new Dictionary<string, object?>
                {
                    ["status"] = "valid",
                    ["certificates"] = c.Chain.Select(Convert.ToBase64String).ToArray(),
                    ["issuerDN"] = c.IssuerDn,
                    ["SerialNumber"] = c.SerialNumber,
                    ["subjectDN"] = c.SubjectDn,
                    ["validFrom"] = c.ValidFrom,
                    ["validTo"] = c.ValidTo,
                },
                ["authMode"] = "implicit",
                ["multisign"] = "2147483647",
                ["credential_id"] = c.CredentialId,
                ["SCAL"] = "2",
            }).ToArray();
            await Write(ctx, 200, JsonSerializer.Serialize(array));
        });

        Map("signHash", async (ctx, body) =>
        {
            using var doc = JsonDocument.Parse(body);
            var hashes = doc.RootElement.GetProperty("hash").EnumerateArray().Select(e => e.GetString()!).ToList();
            var asyncCode = doc.RootElement.GetProperty("async").GetInt32();
            if (asyncCode == 0)
            {
                var signed = self!._scenario.DropLastSignature ? hashes.Take(hashes.Count - 1) : hashes;
                var sigs = signed.Select(h => self.EncodeSignature(SignatureFor(h))).ToArray();
                await Write(ctx, 200, JsonSerializer.Serialize(new { signatures = sigs }));
                return;
            }

            if (self!._scenario.OmitTransactionId)
            {
                await Write(ctx, 200, "{}");
                return;
            }

            var tx = "tx-" + Interlocked.Increment(ref self._txSeq);
            self._hashesByTx[tx] = hashes;
            await Write(ctx, 200, JsonSerializer.Serialize(new { transactionId = tx }));
        });

        Map("requests/status", async (ctx, body) =>
        {
            using var doc = JsonDocument.Parse(body);
            var tx = doc.RootElement.GetProperty("transactionId").GetString()!;
            var call = self!._statusCallsByTx.AddOrUpdate(tx, 1, (_, n) => n + 1);
            var seq = self._scenario.StatusSequence.TryGetValue(tx, out var s) ? s : ["1"];
            var status = seq[Math.Min(call, seq.Count) - 1];
            if (status == "1" && self._hashesByTx.TryGetValue(tx, out var hashes))
            {
                var kept = self._scenario.DropLastSignature ? hashes.Take(hashes.Count - 1) : hashes;
                var sigs = kept.Select(h => self.EncodeSignature(SignatureFor(h))).ToArray();
                await Write(ctx, 200, JsonSerializer.Serialize(new { signatures = sigs, status }));
                return;
            }

            await Write(ctx, 200, JsonSerializer.Serialize(new { status }));
        });

        await app.StartAsync();
        var address = app.Urls.First();
        self = new FakeMySignServer(app, address.TrimEnd('/'));
        return self;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private string EncodeSignature(byte[] sig)
    {
        var b64 = Convert.ToBase64String(sig);
        if (!_scenario.WrapSignaturesWithCrLf || b64.Length < 8)
        {
            return b64;
        }

        return b64[..4] + "\r\n" + b64[4..8] + "\r\n" + b64[8..];
    }

    private bool TryConsumeFailure(string route, out int status)
    {
        lock (_lock)
        {
            if (_scenario.FailTimesByRoute.TryGetValue(route, out var entry) && entry.Remaining > 0)
            {
                _scenario.FailTimesByRoute[route] = (entry.Status, entry.Remaining - 1);
                status = entry.Status;
                return true;
            }
        }

        status = 0;
        return false;
    }

    private bool BearerIsValid(string? authorization)
    {
        const string prefix = "Bearer ";
        return authorization is not null
            && authorization.StartsWith(prefix, StringComparison.Ordinal)
            && _issuedTokens.ContainsKey(authorization[prefix.Length..]);
    }

    private static async Task Write(HttpContext ctx, int status, string body)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(body);
    }
}
