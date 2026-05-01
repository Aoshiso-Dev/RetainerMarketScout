# FF14 Retainer Market Scout

Universalis のマーケットデータを見て、リテイナーベンチャー候補を現在のギル価値順に並べる C# WPF デスクトップアプリです。

MVVM は `CommunityToolkit.Mvvm`、DI は `Microsoft.Extensions.DependencyInjection`、構成は Clean Architecture を意識したフォルダ分けにしています。

## 起動

Windows に .NET 8 SDK 以上を入れてから実行します。

```powershell
dotnet run
```

## 使い方

1. `Elemental`、`Tonberry`、`Mana` などのワールド名またはDC名を入力します。
2. `更新` を押します。
3. `retainer_items.csv` の候補ごとに `https://universalis.app/api/v2/{world_or_dc}/{item_id}` を呼び出します。
4. 直近販売平均、推定取得数、直近販売数をもとにした `Score` の高い順で表示します。
5. 一番高い候補は上部の大きなカードに表示されます。
6. 園芸、採掘、戦闘それぞれの上位3件をカードで確認できます。
7. 行をダブルクリックすると、該当アイテムを Universalis で開きます。

## ExpressVPN MCPオプション

`更新前にExpressVPN MCPで接続する` をONにすると、Universalisへ接続する前にExpressVPNのローカルMCPサーバーへ接続処理を依頼します。

- 既定値は `http://127.0.0.1:20090/mcp|smart` です。
- `|` の左側がMCPエンドポイント、右側が接続先です。
- 接続先に `Japan - Tokyo` などを入力できます。
- 接続に失敗した場合、マーケットデータ取得は行わずステータスバーにエラーを表示します。
- ExpressVPN側でMCPサーバー機能を有効化しておく必要があります。

## 候補アイテムの追加

`retainer_items.csv` を編集してください。現在のCSVは次の列に対応しています。

- `カテゴリ`: `園芸`、`採掘`、`戦闘`
- `レベル`: ベンチャーのレベル
- `素材名`: アイテム名
- `必要条件種別`: `必要獲得力` や `必要IL`
- `必要値`: 必要条件の数値

アイテムIDがないCSVでも、更新時にXIVAPIで日本語名からアイテムIDを解決してUniversalisへ問い合わせます。

## アイテム画像キャッシュ

XIVAPIのアイコンアセットをPNGとして取得し、次のフォルダにキャッシュします。

```text
%LocalAppData%\RetainerMarketScout\ItemIcons
```

同じアイテム画像は次回以降ローカルキャッシュを使います。

## データ元

- Universalis: https://universalis.app/
- XIVAPI v2: https://v2.xivapi.com/
- API endpoint: `https://universalis.app/api/v2/{world_or_dc}/{item_id}`

Universalis のマーケット情報はユーザー投稿ベースなので、更新が古いアイテムは `更新日時` を見て判断してください。

## アーキテクチャ

- `Domain`: アプリで使うエンティティ (`CandidateItem`, `MarketResult`)
- `Application`: インターフェイスと `RankRetainerTargetsUseCase`
- `Infrastructure`: CSV読み込み、Universalis API アクセス、ExpressVPN MCP通信
- `Presentation`: WPF ViewModel
- `App.xaml.cs`: DI の Composition Root
