namespace ViettelConnect.MySign.Application.Abstractions;

internal interface IDelayer
{
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}
