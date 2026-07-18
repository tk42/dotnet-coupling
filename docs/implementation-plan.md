# 実装計画 — dotnet-coupling

確定スコア定義は [scoring.md](scoring.md)、レビュー判定は [review.md](review.md) を参照。
本計画は元仕様 §28（MVP）/ §29（ロードマップ）を、レビュー結果で補正したもの。

---

## 0. 技術スタック / 前提

> 環境確定（v1）: 解析対象は **.NET Framework 4.7.2 / レガシー non-SDK（packages.config）**。
> このため **ツール本体も net472** とし、VS/Build Tools の Framework MSBuild で読み込む。
> semantic mode は **Windows 専用**（仕様 §5.3 の制約を正式採用）。
> 開発機: .NET SDK 10 + Visual Studio 18 Community（4.7.2 参照アセンブリ有）。

| 項目        | 採用 |
|-------------|------|
| ツール TFM  | **net472**（レガシー non-SDK の semantic 読込が最も堅牢） |
| 参照解決    | `Microsoft.NETFramework.ReferenceAssemblies`（CI/再現性のため明示参照） |
| 解析        | Roslyn `Microsoft.CodeAnalysis.CSharp.Workspaces` + `MSBuildWorkspace` |
| MSBuild     | `Microsoft.Build.Locator` で VS/Build Tools（Framework MSBuild）を1回登録 |
| Git         | `LibGit2Sharp`（履歴・diff） |
| CLI         | `System.CommandLine`（netstandard2.0 対応版を pin） |
| 設定        | `YamlDotNet`（`dotnet-coupling.yml`） |
| 出力        | `System.Text.Json`（NuGet, net472 対応） |
| テスト      | xUnit + Verify（snapshot） |
| 配布        | **単一マージ exe**（ILRepack/Costura）。net472 は self-contained 不可のため、Framework ランタイム＋VS MSBuild は解析マシン側前提 |

不変条件: **Analysis Core は UI 非依存。すべての出力は CouplingReport(JSON) から派生。**

### net472 採用に伴う注意
- **self-contained 不可**: .NET Framework はランタイム同梱不可。解析マシンに 4.7.2 ランタイム必須（確認済）。
- **MSBuildLocator は VS/Build Tools を登録**: bare な `Framework64\v4.0.30319\MSBuild` 単独では
  Locator が検出できない場合がある。検出失敗時は syntax-only へ降格しメタデータに Reason を記録。
- **CI は Windows runner 必須**（ubuntu では semantic 不可）。GitHub Actions サンプルは `windows-latest` に変更。
- Roslyn / System.CommandLine は net472 対応バージョンを restore 時に確認・pin する。

---

## 1. ソリューション構成

元仕様 §9 を踏襲。MVP 時点で必要なプロジェクトのみ先行作成し、残りは空 csproj を置かない。

```
src/
  DotnetCoupling.Model/        # record 群（依存ゼロ）
  DotnetCoupling.Analysis/     # Roslyn 解析
  DotnetCoupling.Git/          # LibGit2Sharp
  DotnetCoupling.Scoring/      # strength/distance/volatility/risk/grade
  DotnetCoupling.Rules/        # IArchitectureRule 群
  DotnetCoupling.Output/       # Console/Json/Markdown(AI)/Sarif
  DotnetCoupling.Cli/          # System.CommandLine（薄く）
tests/
  DotnetCoupling.Tests/        # unit + snapshot
  DotnetCoupling.IntegrationTests/
samples/
  SimpleConsoleApp/
  AspNetCoreApp/
  MultiProjectCleanArchitecture/
  LegacyLayeredApp/
```

依存方向: `Cli → Output → {Rules, Scoring} → {Analysis, Git} → Model`。
Model は他に依存しない（自己ドッグフーディングの基準）。

---

## 2. Model（確定差分）

元仕様 §11 から以下を変更（[review.md](review.md) M1）。

```csharp
public sealed record Occurrence(DependencyKind Kind, string File, int Line, string? Evidence);

public sealed record CouplingEdge(
    string SourceId,
    string TargetId,
    double Strength,           // = max(occurrences.kind strength)
    double Distance,
    double Volatility,         // unknown は double.NaN で表現
    double Risk,
    double StructuralRisk,     // CI 一次判定用（volatility 非依存）
    double Confidence,
    IReadOnlyList<Occurrence> Occurrences
);
```

`CouplingReport` に `AnalysisMetadata{ Mode, Confidence, Reason }` を必須で持たせる。

---

## 3. フェーズ別計画

> **ロードマップ再編（2026-07-18）**: Phase 1 完了後、方針を「解析コアの正確性を最優先」に変更した。
> 新順序は **Track A（解析コア）→ Track C（ドキュメント・公開）**。
> 旧 Phase 2–5（CI 連携 / Web レポート / AI 出力 / MCP Server）は **Track B** として
> ロードマップから除外し、本節末尾にアーカイブとして保持する（将来再開の可能性は残す）。

### Phase 1 — CLI MVP（最優先）

**目的:** まず使える診断 CLI。`.sln` を読み、危険な具象依存を出す。

実装単位（おおむね着手順）:

1. **Model 一式** — record + enum。単体で完結。
2. **SolutionLoader** — MSBuildLocator 登録 → `MSBuildWorkspace.OpenSolutionAsync`。
   `WorkspaceFailed` を収集し、落ちた project は warning として継続（§26.3）。
3. **SemanticAnalyzer / CouplingWalker** — 各 Document の SemanticModel を取り、
   §11.4 の DependencyKind を抽出。出現ごとに Occurrence を積む。
4. **SyntaxOnlyAnalyzer** — フォールバック。using/型宣言/baseType の文字列依存のみ。
   **Risk は計算しない**（[review.md](review.md) M2）。
5. **auto mode** — semantic 試行 → 失敗で syntax-only。Reason をメタデータに記録。
6. **Git: FileVolatilityAnalyzer** — 直近90日の変更回数を file 単位で一括取得（S5）。
   履歴なしは unknown。
7. **Scoring** — strength(S3) / distance(S4) / volatility(S5) / risk(S1) / structuralRisk /
   repo 集約(S2) / grade。**ここを最初に純関数 + 単体テストで固める。**
8. **Rules（MVP3種）** — LayerViolation / CircularDependency / ConcreteDependency。
   循環は Tarjan SCC（project/namespace/type 各粒度）。
9. **Output（MVP）** — Console(summary/hotspots) / Json。
10. **CLI** — `--summary` `--hotspots` `--json` `--check --min-grade`。

完了基準（§30.1）:
- 典型 .NET solution を解析し repo score/grade を出す
- 具象 infra 依存（Web→Sql 実装）を hotspot 検出
- project 間依存を JSON で出力
- Git 変更頻度が risk に反映
- `--check` が exit code で合否（§23.2: 0/1/2/3/9）
- semantic 失敗時に syntax-only フォールバック

### Track A — 解析コア（現在の最優先）

**目的:** 既存抽出の見落とし・過小評価を正し、依存グラフの正確性を底上げする。
方針決定は 2026-07-18 の設計インタビューで確定（下表）。

**A-1. 精度バグ修正 — ジェネリック型引数のドロップ**
- 現状 `CouplingWalker.ResolveNamedTypes` が `named.OriginalDefinition` を返すため、
  `List<Order>` は `List<T>` に解決され **型引数 `Order` への依存が失われる**。
- 修正: `INamedTypeSymbol.TypeArguments` を再帰的に辿り、拾った型引数を
  `GenericArgument`（strength 0.45 固定）として emit。container は従来の文脈 kind を維持。
  ネストジェネリック（`Dictionary<string, List<Order>>`）も同一ルールで再帰拾い。

**A-2. 未実装依存種別の実装（4種）**

| 種別 | 検出方針 | strength |
|------|----------|---------:|
| `DiRegistration` | `Add{Scoped,Singleton,Transient}` かつレシーバが `IServiceCollection` 実装。**合成起点（登録呼び出しの外側の型）→ 具象実装** を記録。カバーは MS DI 主要オーバーロード（2型引数 / 1型引数自己束縛 / `typeof` ペア）に限定。Autofac 等・keyed・`AddDbContext` 等の固有拡張は見送り。 | 0.50 |
| `GenericArgument` | A-1 と一体（型引数の再帰拾い）。 | 0.45 |
| `StaticAccess` | 静的 `IFieldSymbol`/`IPropertySymbol` への `MemberAccessExpression` を新規に拾う。**静的メソッド呼び出しは `MethodCall` のまま**（既存スナップショットへの影響最小化）。 | 0.60 |
| `Attribute` | `type.GetAttributes()` と各メンバーの `GetAttributes()` → `AttributeClass` を記録（**型＋メンバーレベル**、パラメータ属性は見送り）。 | 0.30 |

> 見送り: `Reflection` / `DynamicAccess`（ヒューリスティック頼みで偽陽性リスク・低信頼）、
> `UsingDirective`（strength 0.10 で情報価値が薄い）。

**A-3. 影響と後方互換**
- エッジ strength は `ForEdge = max(kind strength)`。既存エッジに弱い種別を足しても強度は不変で、
  スコア変化は主に **新規エッジ**（DI 具象束縛・他で参照されない型引数）から生じる。
- DI 露出により `ConcreteDependencyRule` の違反が増え、DI 多用のコードベースはグレードが下がり得る。
  これを **より正確なスコアとして正**とし、**既定有効・version bump**（`Directory.Build.props`）で扱う。
- 同期: `docs/scoring.md`・README のスコア表に `DiRegistration`/`Reflection`/`DynamicAccess` を追記し
  「実装済み種別」を明記。スナップショット（Verify）再生成。

**A-4. テスト**
- `CouplingWalker` はインメモリ `Compilation` でテスト可能（クラス設計どおり）。
  4種それぞれの focused ユニットテストを追加（ジェネリックのネスト / DI 各オーバーロード /
  静的 field・property / 型・メンバー属性）。
- `samples/LegacyLayeredApp` の統合スナップショットを再生成。

### Track C — ドキュメント・公開（次フェーズ）

**目的:** OSS 公開と Blume ベースのドキュメントサイト構築。Track A 完了後に着手。
方針は 2026-07-18 の設計インタビューで確定。

- **C-1. OSS 公開準備（GitHub Pages の前提）**: `LICENSE`（**MIT**）追加・README の `TBD` 更新、
  `gitleaks` 等で全履歴のシークレット走査、内部ドキュメントの公開可否を目視確認。
  → private→public のフリップはメンテナが手動実行 → Pages 有効化。
  （private リポでは Pages が有料プラン必須のため、公開 → Pages の順序は固定。）
- **C-2. サイト基盤**: 同リポの `website/` に `npx blume init`。`blume.config.ts` に
  `base`（プロジェクトページのサブパス配信）・日英 i18n・サイトメタを設定。
  `.gitignore` に `website/node_modules`・`website/dist`、Node ツールチェーン固定（`.nvmrc` 等）、
  GitHub Actions（`website/**` 変更で build → Pages デプロイ、`workflow_dispatch` 併設）。
- **C-3. コンテンツ（MDX、日→英 i18n）**: Getting Started（README から移植）/
  CLI リファレンス（全オプション・終了コード・Tabs/Procedure 活用）/
  スコアリング仕様（`scoring.md` ベース、KaTeX 数式・Mermaid 依存方向図）。
  README は簡潔なランディング（バッジ・クイックスタート・サイト誘導）へ再構成。

---

### Track B（アーカイブ / ロードマップ除外）— 旧 Phase 2–5

> 解析コアを優先するため、以下はロードマップから外した。検討の経緯として保持する。

#### 旧 Phase 2 — CI 対応
- `--diff <base>`（DiffAnalyzer, LibGit2Sharp）/ `--baseline`
- SARIF 出力（SarifReporter）
- GitHub Actions サンプル
- **ゲート条件確定**（下記 CI 節）

#### 旧 Phase 3 — Web レポート（静的 HTML）
- StaticReportGenerator（report.json + graph.json + index.html、サーバー不要）
- Project Graph / Namespace Graph / Hotspots / Impact / Diff View

#### 旧 Phase 4 — AI 出力
- `ai-context.md` / `ai-context.json`（§21）
- files-to-read / suggested refactoring / constraints / verification commands

#### 旧 Phase 5 — MCP Server
- `dotnet-coupling mcp` — get_summary / get_hotspots / get_impact /
  get_refactor_plan / get_files_to_read / get_diff_report

---

## 4. CI ゲート条件（[review.md](review.md) M5 確定）

`--check` の合否判定。複数条件は **OR で fail**。

| 条件 | 既定 | 説明 |
|------|------|------|
| grade < `--min-grade` | 有効 | structuralRisk ベース score（S2/S3） |
| 新規 LayerViolation (error) | 有効 | `--diff` 指定時のみ評価 |
| 新規 CircularDependency | 有効 | 同上 |
| grade が baseline より低下 | `--no-regression` 時 | `--diff`/`--baseline` 必要 |
| volatility 込みで判定 | `--include-volatility-in-gate` | 既定 off（C3 安定化） |

Exit code: 0=合格 / 1=ゲート不合格 / 2=設定エラー / 3=解析失敗 / 9=内部エラー。

---

## 5. テスト方針

- **Scoring は純関数化して unit を厚く**（S1–S6 の検算ケースを固定。看板例 0.60 を回帰テスト化）。
- Rules は最小サンプルで判定境界をテスト。
- IntegrationTests: 4 sample solution（SimpleConsole / AspNetCore / CleanArchitecture / LegacyLayered）を解析。
- Snapshot(Verify): console / json / ai-markdown / sarif。
- syntax-only fallback / Git volatility / DI 解決の決定表（付録A）を個別検証。

---

## 6. 既知のリスクと対処

| リスク | 対処 |
|--------|------|
| MSBuildWorkspace の部分ロード失敗 | WorkspaceFailed 収集 + project 単位継続（§26.3） |
| Windows 専用 MSBuild 依存 project を Linux 解析 | semantic 失敗 → syntax-only に降格、Reason 明記 |
| 初回 compilation の性能 | project 並列 + SemanticModel を document 単位キャッシュ。MVP 目標は「数分」 |
| partial class 跨ぎ | semantic で吸収。syntax-only は low confidence と明記 |
| volatility の repo 相対揺れ | 絶対スケール（S5）+ CI は structuralRisk 既定 |

---

## 7. 直近の着手順（Phase 1 内）

1. ✅ **完了** ソリューション + `Model`（採用スキャフォルドを net472 化）+ `Scoring` 純関数、unit 26件 green（看板例 0.603 回帰含む）。
2. 🔄 **進行中** `CouplingWalker`（`Compilation` 入力）+ `SemanticGraphBuilder` 実装済、in-memory テスト green（看板例構造を再現）。`SolutionLoader`/`MsBuildRegistrar`（MSBuildWorkspace）は実装済だが、開発機の VS2026 プレビューが新しすぎ in-process ロードが BCL 不整合で失敗（[review.md] 環境メモ）。実ロード E2E は VS2019/2022 系の互換マシンで検証する。
3. 🔄 `Git` volatility 結線: **純ロジック `VolatilityEnricher`（Scoring）完成・テスト3件 green（git非依存）**。`FileVolatilityAnalyzer`/`GitRepositoryDetector`（LibGit2Sharp）+ CLI 結線済だが、**LibGit2Sharp の実行検証は未実施**（互換マシンの E2E で。実 git を汚さない方針）。
4. ✅ **完了** Rules 3種（Layer/Circular/Concrete）+ HotspotExtractor + ReportBuilder + Console/Json 出力。
5. ✅ **完了** CLI 本パイプライン結線、`--summary/--hotspots/--json/--check --min-grade`、exit code（0/2/3、ゲート不合格=1）。
6. ⬜ 4 sample で integration green（**VS2026 で実 MSBuild ロード不可のため、互換マシン（VS2019/2022）で実施**）。

現状テスト: unit/component 39件 green（Scoring 26 + Walker 4 + Rules/Hotspot 5 + Output 4）。すべて in-memory コンパイルで MSBuild 非依存。

ステップ1（Model + Scoring + unit）から着手するのが、後続の出力・ルールすべての基準になる。
