using System.Windows;
using RetainerMarketScout.Application.Abstractions;
using RetainerMarketScout.Application.UseCases;
using RetainerMarketScout.Infrastructure.Csv;
using RetainerMarketScout.Infrastructure.ExpressVpn;
using RetainerMarketScout.Infrastructure.Universalis;
using RetainerMarketScout.Infrastructure.XivApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RetainerMarketScout.Presentation.ViewModels;

namespace RetainerMarketScout;

public partial class App : System.Windows.Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddHttpClient<IMarketPriceProvider, UniversalisMarketPriceProvider>(client =>
                {
                    client.BaseAddress = new Uri("https://universalis.app/api/v2/");
                    client.Timeout = TimeSpan.FromSeconds(25);
                });
                services.AddHttpClient<IExpressVpnClient, ExpressVpnMcpClient>(client =>
                {
                    client.Timeout = TimeSpan.FromSeconds(45);
                });
                services.AddHttpClient<IItemIdResolver, XivApiItemIdResolver>(client =>
                {
                    client.BaseAddress = new Uri("https://v2.xivapi.com/api/");
                    client.Timeout = TimeSpan.FromSeconds(25);
                });

                services.AddSingleton<IItemCandidateRepository, CsvItemCandidateRepository>();
                services.AddSingleton<RankRetainerTargetsUseCase>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        base.OnExit(e);
    }
}
