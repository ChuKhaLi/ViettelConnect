using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>
/// Hỏi requests/status mỗi <c>interval</c> cho tới khi có kết cục hoặc hết <c>totalTimeout</c>.
/// Trạng thái lạ/rỗng được coi là đang chờ (không bao giờ là thành công, không treo vô hạn).
/// </summary>
internal sealed class PollSignStatus
{
    private readonly IMySignWireClient _wire;
    private readonly IDelayer _delayer;
    private readonly TimeProvider _time;
    private readonly ICorrelationIdAccessor _correlation;

    public PollSignStatus(IMySignWireClient wire, IDelayer delayer, TimeProvider time, ICorrelationIdAccessor correlation)
    {
        _wire = wire ?? throw new ArgumentNullException(nameof(wire));
        _delayer = delayer ?? throw new ArgumentNullException(nameof(delayer));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _correlation = correlation ?? throw new ArgumentNullException(nameof(correlation));
    }

    /// <summary>Trả danh sách chữ ký thô khi thành công; ném <see cref="MySignException"/> ở mọi kết cục khác.</summary>
    public async Task<IReadOnlyList<byte[]>> ExecuteAsync(
        MySignCredentials who,
        string transactionId,
        TimeSpan interval,
        TimeSpan totalTimeout,
        CancellationToken ct)
    {
        var start = _time.GetUtcNow();
        var deadline = start + totalTimeout;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var snapshot = await _wire.GetStatusAsync(who, transactionId, ct).ConfigureAwait(false);

            switch (snapshot.Status)
            {
                case SignStatus.Succeeded:
                    return snapshot.Signatures;

                case SignStatus.Rejected:
                    throw new MySignException(
                        MySignErrorCategory.SignRejected,
                        snapshot.RawStatus,
                        "Người ký đã từ chối yêu cầu ký.",
                        _correlation.Current);

                case SignStatus.TimedOut:
                    throw new MySignTimeoutException(
                        "Yêu cầu ký đã hết hạn chờ người ký xác nhận.",
                        _correlation.Current,
                        transactionId,
                        snapshot.RawStatus);

                case SignStatus.Failed:
                    throw new MySignException(
                        FailedCategory(snapshot.RawStatus),
                        snapshot.RawStatus,
                        $"Yêu cầu ký thất bại (mã trạng thái {snapshot.RawStatus}).",
                        _correlation.Current);

                case SignStatus.Pending:
                default:
                    break;
            }

            var now = _time.GetUtcNow();
            if (now >= deadline)
            {
                throw new MySignTimeoutException(
                    $"Hết thời gian chờ ({totalTimeout}) mà yêu cầu ký chưa có kết quả.",
                    _correlation.Current,
                    transactionId);
            }

            var remaining = deadline - now;
            await _delayer.DelayAsync(interval < remaining ? interval : remaining, ct).ConfigureAwait(false);
        }
    }

    private static MySignErrorCategory FailedCategory(string? rawStatus) => rawStatus switch
    {
        "4005" => MySignErrorCategory.InsufficientQuota,
        "4006" or "13003" or "13004" => MySignErrorCategory.CertificateInvalid,
        _ => MySignErrorCategory.SignFailed,
    };
}
