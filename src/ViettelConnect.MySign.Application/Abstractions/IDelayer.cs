namespace ViettelConnect.MySign.Application.Abstractions;

public interface IDelayer
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}
