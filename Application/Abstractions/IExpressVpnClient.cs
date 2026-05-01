namespace RetainerMarketScout.Application.Abstractions;

public interface IExpressVpnClient
{
    Task<ExpressVpnConnectionResult> EnsureConnectedAsync(string? location, CancellationToken cancellationToken);
}
