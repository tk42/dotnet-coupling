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
| GenericArgument | 0.45 |
| Attribute | 0.30 |
| UsingDirective | 0.10 |

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

- [x] **Phase 1** — CLI MVP（セマンティック解析・スコアリング・ホットスポット・ルール・出力）
- [ ] **Phase 2** — CI 対応（`--diff` / `--baseline` / SARIF 出力 / GitHub Actions サンプル）
- [ ] **Phase 3** — Web レポート（静的 HTML グラフ可視化）
- [ ] **Phase 4** — AI 出力（`ai-context.md` / リファクタリング提案）
- [ ] **Phase 5** — MCP Server（エージェント向け API）

---

## ドキュメント

| ドキュメント | 内容 |
|---|---|
| [docs/scoring.md](docs/scoring.md) | スコアリング確定仕様（Risk / Grade / Strength / Distance / Volatility） |
| [docs/implementation-plan.md](docs/implementation-plan.md) | 実装計画・フェーズ別詳細 |
| [docs/review.md](docs/review.md) | 仕様レビュー記録・設計判断の根拠 |

---

## ライセンス

TBD
