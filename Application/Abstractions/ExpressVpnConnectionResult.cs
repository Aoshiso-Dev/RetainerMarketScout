namespace RetainerMarketScout.Application.Abstractions;

public sealed class ExpressVpnConnectionResult
{
    public required bool IsConnected { get; init; }
    public required string Message { get; init; }
}
