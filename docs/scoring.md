# スコアリング確定仕様 — dotnet-coupling

本書はレビュー（[review.md](review.md)）で確定したスコア定義を正とする。
元仕様 §12–16 の記述と差異がある箇所は本書が優先する。

---

## S1. Edge Risk（確定式）

```
risk = strength × (0.5 + 0.5·distance) × (0.5 + 0.5·volatility)
```

- `strength ∈ [0,1]`、`distance ∈ [0,1]`、`volatility ∈ [0,1]`
- distance / volatility は **softening** して適用（0 でも係数 0.5 を残す）。
  これにより「同一namespace内の強依存」が機械的に低risk化されすぎるのを防ぐ。
- `risk ∈ [0,1]`（最大 = 1×1×1）。
- 検算: S=0.85, D=0.65, V=0.72 → 0.85×0.825×0.86 = **0.603**（看板例と一致）。

### 構造 Risk（CI 一次判定用）
volatility を含めない決定論的な risk。CI ゲートの安定性のため使用。
```
structuralRisk = strength × (0.5 + 0.5·distance)
```

---

## S2. Repository Score / Grade（集約）

エッジ risk 集合 `R` に対し、上位 20% の平均（CVaR 的）を repo risk とする。

```
r̄ = mean( top 20% of R by risk )        # 件数が5未満なら全件平均
repoScore = round( 100 × (1 − r̄) )
```

単純平均だと大量の自然な弱依存に薄められて鈍感化するため、危険側の裾を見る。

| Score | Grade |
|------:|:-----:|
| 90–100 | A |
| 75–89  | B |
| 60–74  | C |
| 40–59  | D |
| 0–39   | F |

CI の `--min-grade` 判定は、デフォルトで **structuralRisk ベースの score**（S3）を使う。
`--include-volatility-in-gate` 指定時のみ volatility 込み score で判定。

---

## S3. Integration Strength（kind 別・確定値）

| DependencyKind          | Strength |
|-------------------------|---------:|
| Inheritance             | 1.00 |
| InterfaceImplementation | 0.90 |
| FieldType               | 0.85 |
| PropertyType            | 0.80 |
| ConstructorParameter    | 0.75 |
| ObjectCreation          | 0.70 |
| ReturnType              | 0.65 |
| MethodParameter         | 0.65 |
| StaticAccess            | 0.60 |
| MethodCall              | 0.50 |
| GenericArgument         | 0.45 |
| Attribute               | 0.30 |
| DiRegistration          | 付録A |
| UsingDirective          | 0.10 |
| Reflection              | 付録A |
| DynamicAccess           | 付録A |

同一 (source,target) に複数 kind が併存する場合、
**エッジ strength = max(各 occurrence の kind strength)**。各出現は `Occurrences[]` に保持。

---

## S4. Distance（確定）

| 区分                          | 値   |
|-------------------------------|-----:|
| 同一 Type 内                  | 0.00 |
| 同一 File 内                  | 0.10 |
| 同一 Namespace 内             | 0.25 |
| 同一 Project 内               | 0.40 |
| 同一 Solution 内・別 Project  | 0.65 |
| 社内共通 Library              | 0.75 |
| 外部 Assembly / NuGet         | 0.85 |

**変更点（C4）:** 「禁止越境 → 1.0」は撤回。レイヤー違反は distance ではなく
`LayerViolationRule`（離散・error）で扱う。distance は純粋に構造的距離を表す。
レイヤー方向は「論理距離の補正」として最大 +0.10 までの微補正に留める（越境固定はしない）。

---

## S5. Volatility（絶対スケール）

Git 履歴から直近 90 日窓の変更回数 `c90` を取得し、**固定基準**で正規化。

```
volatility = min(1.0, log(1 + c90) / log(1 + V_FULL))    # V_FULL = 10（既定・設定可）
```

- repo 相対の `maxChangeCount` は使わない（CI ゲート安定化, C3）。
- Git 履歴なし → `volatility = unknown`（risk 計算では 0 相当、`Confidence: medium`）。
- 将来拡張: 直近重み付け / churn / co-change / bug-fix commit 頻度。

---

## S6. Hotspot 判定（確定）

```
risk ≥ 0.60  AND  strength ≥ 0.50  AND  distance ≥ 0.50
```
（元仕様 §16 を踏襲。S1 の式で看板例 0.60 が境界を満たす。）

---

## S7. Confidence

- エッジ confidence: semantic 解決済 = high / DI・推論 = medium / 文字列マッチ = low。
- repo confidence: low エッジ比率 `p` で減衰。`p>0.5 → low`, `0.2<p≤0.5 → medium`, それ以外 high。
- syntax-only モードは repo confidence を強制 low、Risk/Grade を出力しない（M2）。

---

## 付録 A. DI / Reflection 決定表

| パターン                                              | Kind          | Strength | Confidence |
|-------------------------------------------------------|---------------|---------:|:----------:|
| `AddScoped<I, Impl>()`（ジェネリック・解決可）        | DiRegistration| 0.50     | high       |
| `AddScoped(typeof(I), typeof(Impl))`                  | DiRegistration| 0.45     | medium     |
| `AddScoped<I>(sp => new Impl(...))`（factory）        | DiRegistration| 0.40     | low        |
| `AddScoped(typeof(I), assemblyScanResult)`（scan）    | DiRegistration| 0.30     | low        |
| `Activator.CreateInstance(typeof(T))`                 | Reflection    | 0.30     | low        |
| `GetType().GetMethod(...).Invoke(...)`                | Reflection    | 0.20     | low        |
| `dynamic` 経由アクセス                                | DynamicAccess | 0.20     | low        |
| 文字列型名からの解決（`Type.GetType("…")`）           | Reflection    | 0.10     | low        |
