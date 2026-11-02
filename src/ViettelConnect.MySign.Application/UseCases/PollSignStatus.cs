using ViettelConnect.MySign.Application.Abstractions;
using ViettelConnect.MySign.Domain.Errors;
using ViettelConnect.MySign.Domain.Signing;

namespace ViettelConnect.MySign.Application.UseCases;

/// <summary>
/// Hỏi requests/status mỗi <c>interval</c> cho tới khi có kết cục hoặc hết <c>totalTimeout</c>.
/// Trạng thái lạ/rỗng, và lỗi kết nối tạm thời khi hỏi, được coi là đang chờ (không bao giờ là thành công, không treo
/// vô hạn). Mỗi lần hỏi bị huỷ khi tới hạn chót (lời gọi treo không kéo thời gian tổng quá <c>totalTimeout</c>, trừ
/// ngân sách tối thiểu <see cref="MinPollBudget"/> của lần hỏi cuối). Mọi lỗi ném ra sau khi đã có giao dịch đều mang
/// <see cref="MySignException.TransactionId"/>.
/// </summary>
internal sealed class PollSignStatus
{
    /// <summary>
    /// Ngân sách tối thiểu cho một lần hỏi (lần hỏi cuối đúng hạn chót vẫn có cơ hội trả lời); thời gian tổng tối đa
    /// vì vậy là <c>totalTimeout</c> + chừng này.
    /// </summary>
    internal static readonly TimeSpan MinPollBudget = TimeSpan.FromSeconds(1);

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

    /// <summary>
    /// Trả danh sách chữ ký thô khi thành công; ném <see cref="MySignException"/> ở mọi kết cục khác (hết hạn chót:
    /// <see cref="MySignTimeoutException"/> kèm trạng thái thô cuối cùng nhận được).
    /// </summary>
    public async Task<IReadOnlyList<byte[]>> ExecuteAsync(
        MySignCredentials who,
        string transactionId,
        TimeSpan interval,
        TimeSpan totalTimeout,
        CancellationToken ct)
    {
        var start = _time.GetUtcNow();
        var deadline = start + totalTimeout;
        string? lastRawStatus = null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Yêu cầu đã gửi tới người ký: lỗi kết nối khi hỏi trạng thái (sau khi handler đã thử lại ngắn) không có nghĩa
            // là ký thất bại — người ký vẫn có thể xác nhận. Coi như đang chờ tới hạn chót. Huỷ của người gọi vẫn ném ngay.
            // Mỗi lần hỏi bị chặn bởi thời gian còn lại tới hạn chót (lời gọi treo không kéo dài quá TotalTimeout).
            StatusSnapshot? snapshot;
            var budget = deadline - _time.GetUtcNow();
            using (var budgetCts = new CancellationTokenSource(budget > MinPollBudget ? budget : MinPollBudget, _time))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, budgetCts.Token))
            {
                try
                {
                    snapshot = await _wire.GetStatusAsync(who, transactionId, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (budgetCts.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    throw DeadlineReached(totalTimeout, transactionId, lastRawStatus);
                }
                catch (MySignException ex) when (ex.Category == MySignErrorCategory.Transport && !ct.IsCancellationRequested)
                {
                    if (budgetCts.IsCancellationRequested)
                    {
                        throw DeadlineReached(totalTimeout, transactionId, lastRawStatus);
                    }

                    snapshot = null;
                }
                catch (MySignException ex) when (ex.TransactionId is null)
                {
                    // Lỗi xảy ra sau khi đã có giao dịch (vd chữ ký không phải base64, mã lỗi Viettel khi hỏi trạng thái):
                    // giữ nguyên loại/mã/mô tả/mã tương quan, bổ sung mã giao dịch để đối soát.
                    throw WithTransactionId(ex, transactionId);
                }
            }

            if (snapshot is not null)
            {
                lastRawStatus = snapshot.RawStatus ?? lastRawStatus;
                switch (snapshot.Status)
                {
                    case SignStatus.Succeeded:
                        return snapshot.Signatures;

                    case SignStatus.Rejected:
                        throw new MySignException(
                            MySignErrorCategory.SignRejected,
                            snapshot.RawStatus,
                            "Người ký đã từ chối yêu cầu ký.",
                            _correlation.Current,
                            transactionId: transactionId);

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
                            _correlation.Current,
                            transactionId: transactionId);

                    case SignStatus.Pending:
                    default:
                        break;
                }
            }

            var now = _time.GetUtcNow();
            if (now >= deadline)
            {
                throw DeadlineReached(totalTimeout, transactionId, lastRawStatus);
            }

            var remaining = deadline - now;
            await _delayer.DelayAsync(interval < remaining ? interval : remaining, ct).ConfigureAwait(false);
        }
    }

    private MySignTimeoutException DeadlineReached(TimeSpan totalTimeout, string transactionId, string? lastRawStatus) =>
        new(
            $"Hết thời gian chờ ({totalTimeout}) mà yêu cầu ký chưa có kết quả.",
            _correlation.Current,
            transactionId,
            lastRawStatus);

    private static MySignException WithTransactionId(MySignException ex, string transactionId) =>
        ex is MySignTimeoutException
            ? new MySignTimeoutException(ex.Detail, ex.CorrelationId, transactionId, ex.RawCode, ex)
            : new MySignException(ex.Category, ex.RawCode, ex.Detail, ex.CorrelationId, ex, transactionId);

    private static MySignErrorCategory FailedCategory(string? rawStatus) => rawStatus switch
    {
        "4005" => MySignErrorCategory.InsufficientQuota,
        "4006" or "13003" or "13004" => MySignErrorCategory.CertificateInvalid,
        _ => MySignErrorCategory.SignFailed,
    };
}
