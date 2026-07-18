# dotnet-coupling

> .NET ソリューションの結合度を可視化・スコアリングし、アーキテクチャの健全性を定量評価する静的解析 CLI ツール。

---

## 概要

`dotnet-coupling` は、.NET ソリューション内の型間・プロジェクト間の結合関係を Roslyn セマンティック解析で抽出し、**結合強度（Strength）・構造距離（Distance）・変更頻度（Volatility）** の3軸でリスクをスコアリングします。

CI パイプラインに組み込むことで、アーキテクチャ劣化を定量的に検出し、品質ゲートとして機能します。

### 主な機能

- 🔍 **セマンティック依存解析** — Roslyn `MSBuildWorkspace` による正確な型解決
- 📊 **リスクスコアリング** — Strength × Distance × Volatility の3軸モデル
- 🏷️ **グレード判定** — リポジトリ全体を A〜F で評価（CI ゲート対応）
- 🔥 **ホットスポット検出** — 高リスクの結合をピンポイントで特定
- ⚠️ **ルール違反検出** — レイヤー違反・循環依存・具象依存の3種
- 📁 **複数出力形式** — コンソール / JSON / Markdown
- 🌿 **Git 連携** — 直近90日の変更頻度を Volatility に反映
- 🛡️ **フォールバック** — セマンティック解析失敗時は syntax-only モードに自動降格

---

## クイックスタート

### 前提条件

- **Windows**（セマンティック解析は Windows 専用）
- **.NET Framework 4.7.2** ランタイム
- **Visual Studio** または **Build Tools**（MSBuild が必要）

### ビルド

```powershell
# PowerShell から一式（Restore → Build → Test → Publish）
.\build.ps1

# Release ビルドのみ
.\build.ps1 -Task Build -Configuration Release

# テスト実行
.\build.ps1 -Task Test

# 単体 exe 発行（dist/ に出力）
.\build.ps1 -Task Publish
```

Windows Explorer からは `build.cmd` をダブルクリックでも実行可能です。

### 実行

```powershell
# サマリ表示（既定）
.\dist\dotnet-coupling.exe MyApp.sln --summary

# ホットスポット一覧（上位10件）
.\dist\dotnet-coupling.exe MyApp.sln --hotspots --top 10

# ルール違反一覧
.\dist\dotnet-coupling.exe MyApp.sln --violations

# 全情報を一括表示
.\dist\dotnet-coupling.exe MyApp.sln --verbose

# JSON 出力
.\dist\dotnet-coupling.exe MyApp.sln --json report.json

# Markdown レポート出力
.\dist\dotnet-coupling.exe MyApp.sln --markdown report.md

# CI 品質ゲート（グレード B 未満で exit code 1）
.\dist\dotnet-coupling.exe MyApp.sln --check --min-grade B
```

---

## CLI オプション

| オプション | 説明 |
|---|---|
| `--summary` | 総合サマリを表示（既定） |
| `--hotspots` | ホットスポット（高リスク結合）一覧 |
| `--violations` | ルール違反一覧（レイヤー / 循環 / 具象） |
| `--verbose` | サマリ＋ホットスポット＋違反を一括表示 |
| `--top <N>` | ホットスポットの表示件数を N 件に制限 |
| `--json <path>` | 全情報を JSON で出力 |
| `--markdown <path>` | Markdown レポートを出力 |
| `--check` | 品質ゲート判定（終了コードで合否） |
| `--min-grade <A..F>` | `--check` の合格ライン（既定: B） |

### 終了コード

| Code | 意味 |
|---:|---|
| 0 | 合格 |
| 1 | ゲート不合格 |
| 2 | 設定エラー |
| 3 | 解析失敗 |
| 9 | 内部エラー |

---

## スコアリングモデル

### Edge Risk

```
risk = strength × (0.5 + 0.5 · distance) × (0.5 + 0.5 · volatility)
```

3つの軸はすべて `[0, 1]` の範囲で正規化されます。Distance と Volatility は **softening** 適用（値が 0 でも係数 0.5 を残す）により、同一 namespace 内の強依存が過小評価されるのを防ぎます。

### Repository Score & Grade

```
repo_risk = mean(top 20% of edges by risk)
score     = round(100 × (1 − repo_risk))
```

| Score | Grade |
|------:|:-----:|
| 90–100 | A |
| 75–89 | B |
| 60–74 | C |
| 40–59 | D |
| 0–39 | F |

### Strength（依存種別ごとの結合強度）

| 依存種別 | Strength |
|---|---:|
| Inheritance | 1.00 |
| InterfaceImplementation | 0.90 |
| FieldType | 0.85 |
| PropertyType | 0.80 |
| ConstructorParameter | 0.75 |
| ObjectCreation | 0.70 |
| ReturnType | 0.65 |
| MethodParameter | 0.65 |
| StaticAccess | 0.60 |
| MethodCall | 0.50 |
| DiRegistration | 0.50 |
| GenericArgument | 0.45 |
| Attribute | 0.30 |
| UsingDirective | 0.10 |

> **実装済みの依存種別**: Inheritance / InterfaceImplementation / FieldType / PropertyType /
> ConstructorParameter / MethodParameter / ReturnType / ObjectCreation / MethodCall /
> **DiRegistration** / **GenericArgument** / **StaticAccess** / **Attribute**（太字は Track A で追加）。
> `UsingDirective` / `Reflection` / `DynamicAccess` は強度定義のみで、現状は抽出していません。

### Hotspot 判定

```
risk ≥ 0.60  AND  strength ≥ 0.50  AND  distance ≥ 0.50
```

詳細は [docs/scoring.md](docs/scoring.md) を参照してください。

---

## アーキテクチャ

```
src/
  DotnetCoupling.Model/        # record 群（依存ゼロ）
  DotnetCoupling.Analysis/     # Roslyn セマンティック解析
  DotnetCoupling.Git/          # LibGit2Sharp による Git 履歴取得
  DotnetCoupling.Scoring/      # Strength / Distance / Volatility / Risk / Grade
  DotnetCoupling.Rules/        # IArchitectureRule（Layer / Circular / Concrete）
  DotnetCoupling.Output/       # Console / Json / Markdown レポーター
  DotnetCoupling.Cli/          # CLI エントリポイント
tests/
  DotnetCoupling.Tests/        # ユニットテスト + スナップショット
  DotnetCoupling.IntegrationTests/
samples/
  LegacyLayeredApp/            # テスト用サンプルソリューション
docs/
  implementation-plan.md       # 実装計画
  scoring.md                   # スコアリング確定仕様
  review.md                    # 仕様レビュー記録
```

### 依存方向

```
Cli → Output → {Rules, Scoring} → {Analysis, Git} → Model
```

`Model` は他のプロジェクトに依存しません。

---

## 技術スタック

| 項目 | 採用技術 |
|---|---|
| ターゲットフレームワーク | .NET Framework 4.7.2 |
| 解析エンジン | Roslyn (`Microsoft.CodeAnalysis.CSharp.Workspaces`) |
| MSBuild 連携 | `Microsoft.Build.Locator` |
| Git 履歴 | `LibGit2Sharp` |
| CLI | `System.CommandLine` |
| 設定 | `YamlDotNet` |
| 出力 (JSON) | `System.Text.Json` |
| テスト | xUnit + Verify（スナップショット） |
| 配布 | Costura.Fody による単一 exe |

---

## ロードマップ

Phase 1（CLI MVP）完了後は「解析コアの正確性」を最優先とし、その後ドキュメント・公開へ進めます。

- [x] **Phase 1** — CLI MVP（セマンティック解析・スコアリング・ホットスポット・ルール・出力）

### Track A — 解析コア【現在の最優先】

- [ ] ジェネリック型引数のドロップ修正（`List<Order>` の `Order` を捕捉）
- [ ] 未実装依存種別の実装：`DiRegistration` / `GenericArgument` / `StaticAccess` / `Attribute`
- [ ] スコア表・[docs/scoring.md](docs/scoring.md) の同期、スナップショット再生成、version bump

### Track C — ドキュメント・公開【進行中】

- [x] MIT ライセンス付与・シークレット走査 —（public 化のフリップと Pages 有効化はメンテナが手動で実施）
- [x] Blume ドキュメントサイト基盤（[`website/`](website/) ・ GitHub Pages ・ 日英 i18n ・ [deploy workflow](.github/workflows/docs.yml)）
- [x] コンテンツ整備（Getting Started / CLI リファレンス / スコアリング仕様、日英）

> 旧 Phase 2–5（CI 連携 / Web レポート / AI 出力 / MCP Server）は、解析コアを優先するため
> 一旦ロードマップから外しています。検討経緯・仕様は
> [docs/implementation-plan.md](docs/implementation-plan.md) を参照。

---

## ドキュメント

利用者向けのドキュメントサイト（[Blume](https://useblume.dev) / 日英）を [`website/`](website/) に構築しています。
public 化と GitHub Pages 有効化の後、`https://tk42.github.io/dotnet-coupling/` で公開されます。

ローカルでのプレビュー / ビルド:

```bash
cd website
npm install
npm run dev     # 開発サーバー
npm run build   # 本番ビルド（dist/）
```

### 設計ドキュメント（リポジトリ内）

| ドキュメント | 内容 |
|---|---|
| [docs/scoring.md](docs/scoring.md) | スコアリング確定仕様（Risk / Grade / Strength / Distance / Volatility） |
| [docs/implementation-plan.md](docs/implementation-plan.md) | 実装計画・フェーズ別詳細 |
| [docs/review.md](docs/review.md) | 仕様レビュー記録・設計判断の根拠 |

---

## ライセンス

[MIT License](LICENSE) © 2026 tk42
