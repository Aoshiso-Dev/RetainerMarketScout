using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RetainerMarketScout.Application.UseCases;
using RetainerMarketScout.Domain.Entities;

namespace RetainerMarketScout.Presentation.ViewModels;

public sealed partial class MainWindowViewModel(
    RankRetainerTargetsUseCase rankRetainerTargetsUseCase) : ObservableObject
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshOrCancelCommand))]
    private string selectedDataCenter = "Mana";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshOrCancelCommand))]
    private string selectedWorld = "Titan";

    [ObservableProperty]
    private string statusText = "準備完了";

    [ObservableProperty]
    private bool connectExpressVpnBeforeRefresh = true;

    [ObservableProperty]
    private string expressVpnLocation = "http://127.0.0.1:20090/mcp|smart";

    [ObservableProperty]
    private int maxConcurrentRequests = 4;

    [ObservableProperty]
    private int recentSalesDays = 3;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshOrCancelCommand))]
    private bool isRefreshing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenSelectedMarketCommand))]
    private MarketResult? selectedResult;

    private CancellationTokenSource? refreshCancellationTokenSource;

    public ObservableCollection<MarketResult> Results { get; } = [];
    public ObservableCollection<MarketResult> BotanistResults { get; } = [];
    public ObservableCollection<MarketResult> MinerResults { get; } = [];
    public ObservableCollection<MarketResult> BattleResults { get; } = [];
    public ObservableCollection<MarketResult> TopBotanistResults { get; } = [];
    public ObservableCollection<MarketResult> TopMinerResults { get; } = [];
    public ObservableCollection<MarketResult> TopBattleResults { get; } = [];
    public ObservableCollection<string> DataCenters { get; } = new(["Elemental", "Gaia", "Mana", "Meteor"]);
    public ObservableCollection<string> Worlds { get; } = new(["Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan"]);

    private static readonly Dictionary<string, string[]> WorldsByDataCenter = new()
    {
        ["Elemental"] = ["Aegis", "Atomos", "Carbuncle", "Garuda", "Gungnir", "Kujata", "Tonberry", "Typhon"],
        ["Gaia"] = ["Alexander", "Bahamut", "Durandal", "Fenrir", "Ifrit", "Ridill", "Tiamat", "Ultima"],
        ["Mana"] = ["Anima", "Asura", "Chocobo", "Hades", "Ixion", "Masamune", "Pandaemonium", "Titan"],
        ["Meteor"] = ["Belias", "Mandragora", "Ramuh", "Shinryu", "Unicorn", "Valefor", "Yojimbo", "Zeromus"]
    };

    [ObservableProperty]
    private MarketResult? topResult;

    public string TargetWorldOrDc => string.IsNullOrWhiteSpace(SelectedWorld)
        ? SelectedDataCenter
        : SelectedWorld;

    public string RefreshButtonText => IsRefreshing ? "中止" : "更新";

    partial void OnSelectedDataCenterChanged(string value)
    {
        RefreshWorlds(value);
        RefreshOrCancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedWorldChanged(string value)
    {
        OnPropertyChanged(nameof(TargetWorldOrDc));
        RefreshOrCancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(RefreshButtonText));
    }

    [RelayCommand(CanExecute = nameof(CanRefreshOrCancel))]
    private async Task RefreshOrCancelAsync()
    {
        if (IsRefreshing)
        {
            refreshCancellationTokenSource?.Cancel();
            StatusText = "キャンセル中...";
            return;
        }

        using var cancellationTokenSource = new CancellationTokenSource();
        refreshCancellationTokenSource = cancellationTokenSource;
        IsRefreshing = true;

        try
        {
            await RefreshAsync(cancellationTokenSource.Token);
        }
        finally
        {
            refreshCancellationTokenSource = null;
            IsRefreshing = false;
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        Results.Clear();
        BotanistResults.Clear();
        MinerResults.Clear();
        BattleResults.Clear();
        TopBotanistResults.Clear();
        TopMinerResults.Clear();
        TopBattleResults.Clear();
        TopResult = null;
        StatusText = "取得中...";

        var progress = new Progress<string>(message => StatusText = message);

        try
        {
            var rankedResults = await rankRetainerTargetsUseCase.ExecuteAsync(
                new RetainerRankingOptions
                {
                    WorldOrDc = TargetWorldOrDc,
                    ConnectExpressVpnBeforeRefresh = ConnectExpressVpnBeforeRefresh,
                    ExpressVpnLocation = ExpressVpnLocation,
                    MaxConcurrentRequests = MaxConcurrentRequests,
                    RecentSalesDays = RecentSalesDays
                },
                progress,
                cancellationToken);

            foreach (var result in rankedResults)
            {
                Results.Add(result);
                AddClassResult(result);
            }

            TopResult = rankedResults.FirstOrDefault();
            RefreshTopCards();

            StatusText = $"更新完了: {TargetWorldOrDc.Trim()} / {Results.Count} 件";
        }
        catch (OperationCanceledException)
        {
            StatusText = "キャンセルしました";
        }
        catch (Exception ex)
        {
            StatusText = BuildErrorStatus(ex);
        }
    }

    [RelayCommand]
    private void OpenItemsCsv()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = rankRetainerTargetsUseCase.ItemsPath,
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task CopyItemNameAsync(MarketResult? result)
    {
        if (result is null || string.IsNullOrWhiteSpace(result.Name))
        {
            return;
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    System.Windows.Clipboard.Clear();
                    System.Windows.Clipboard.SetText(result.Name, System.Windows.TextDataFormat.UnicodeText);
                });

                StatusText = $"コピーしました: {result.Name}";
                return;
            }
            catch (Exception ex)
            {
                lastException = ex;
                await Task.Delay(80 * attempt);
            }
        }

        StatusText = $"コピーに失敗しました: {lastException?.Message}";
    }

    [RelayCommand(CanExecute = nameof(CanOpenSelectedMarket))]
    private void OpenSelectedMarket()
    {
        if (SelectedResult?.ItemId is not > 0)
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = $"https://universalis.app/market/{SelectedResult.ItemId}",
            UseShellExecute = true
        });
    }

    private bool CanRefreshOrCancel()
    {
        return IsRefreshing || !string.IsNullOrWhiteSpace(TargetWorldOrDc);
    }

    private bool CanOpenSelectedMarket()
    {
        return SelectedResult?.ItemId is > 0;
    }

    private void AddClassResult(MarketResult result)
    {
        switch (result.Category)
        {
            case "園芸":
                BotanistResults.Add(result);
                break;
            case "採掘":
                MinerResults.Add(result);
                break;
            case "戦闘":
                BattleResults.Add(result);
                break;
        }
    }

    private void RefreshTopCards()
    {
        AddTopCards(TopBotanistResults, BotanistResults);
        AddTopCards(TopMinerResults, MinerResults);
        AddTopCards(TopBattleResults, BattleResults);
    }

    private static void AddTopCards(
        ObservableCollection<MarketResult> destination,
        IEnumerable<MarketResult> source)
    {
        foreach (var result in source.Take(3))
        {
            destination.Add(result);
        }
    }

    private void RefreshWorlds(string dataCenter)
    {
        Worlds.Clear();
        if (WorldsByDataCenter.TryGetValue(dataCenter, out var worlds))
        {
            foreach (var world in worlds)
            {
                Worlds.Add(world);
            }
        }

        if (!Worlds.Contains(SelectedWorld))
        {
            SelectedWorld = Worlds.FirstOrDefault() ?? string.Empty;
        }

        OnPropertyChanged(nameof(TargetWorldOrDc));
    }

    private static string BuildErrorStatus(Exception ex)
    {
        if (!ex.Message.Contains("ExpressVPN MCP", StringComparison.OrdinalIgnoreCase))
        {
            return $"エラー: {ex.Message}";
        }

        return $"ExpressVPN接続に失敗しました: {ex.Message} / ヒント: ExpressVPNベータ版が起動中か、MCPサーバーが有効か、MCP/接続先が http://127.0.0.1:20090/mcp|smart の形式か確認してください。ExpressVPNを使わない場合はチェックをオフにしてください。";
    }
}
