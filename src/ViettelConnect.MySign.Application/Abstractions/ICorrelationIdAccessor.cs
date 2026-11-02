namespace ViettelConnect.MySign.Application.Abstractions;

public interface ICorrelationIdAccessor
{
    string Current { get; }
}
