using System.Collections.Concurrent;
using ViettelConnect.MySign.Application.Abstractions;

namespace ViettelConnect.MySign.Infrastructure.Http;

/// <summary>
/// Single-flight lấy/làm mới token theo từng khoá cache: mỗi khoá tối đa một tác vụ đang chạy, các bên chờ
/// dùng chung kết quả (thành công hoặc lỗi). Hai khoá khác nhau (hai đơn vị) không chặn nhau. Tác vụ dùng chung
/// chạy với <see cref="CancellationToken.None"/>; mỗi bên chờ chỉ huỷ việc chờ của riêng mình. Ô được xoá khi
/// xong để lần 401 sau bắt đầu lượt mới.
/// </summary>
internal sealed class SingleFlightRefresh
{
    private readonly ConcurrentDictionary<string, Lazy<Task<AccessToken>>> _slots = new();

    public Task<AccessToken> RefreshAsync(string cacheKey, Func<CancellationToken, Task<AccessToken>> factory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cacheKey);
        ArgumentNullException.ThrowIfNull(factory);

        Lazy<Task<AccessToken>>? installed = null;
        var slot = _slots.GetOrAdd(cacheKey, _ =>
        {
            installed = new Lazy<Task<AccessToken>>(() => factory(CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication);
            return installed;
        });

        var task = slot.Value;

        if (ReferenceEquals(slot, installed))
        {
            _ = task.ContinueWith(
                _ => _slots.TryRemove(new KeyValuePair<string, Lazy<Task<AccessToken>>>(cacheKey, slot)),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return task.WaitAsync(ct);
    }
}
