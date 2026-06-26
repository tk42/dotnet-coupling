# 仕様レビュー記録 — dotnet-coupling

対象: dotnet-coupling 仕様設計書（v0 ドラフト）
判定: 思想・全体構成は妥当。実装着手前に下記の矛盾・未定義を解消する。

凡例: 🔴 着手前必須 / 🟠 早期に確定 / 🟢 軽微

---

## 🔴 Critical

### C1. スコア式と例の不一致
§15 の式 `risk = strength × distance × (0.5 + 0.5·volatility)` に
§16/§21 の例値 (S=0.85, D=0.65, V=0.72) を代入すると **0.475**。
仕様記載の **0.61** にならず、看板例が自らの hotspot 閾値 0.60 を下回る。

逆算すると例は次式と一致:
`risk = strength × (0.5 + 0.5·distance) × (0.5 + 0.5·volatility) = 0.85×0.825×0.86 ≈ 0.60`

**決定:** distance も softening する式を正とする（[scoring.md](scoring.md) S1）。

### C2. リポジトリ全体スコアの集約が未定義
Risk/Grade はエッジ単位定義のみ。`Overall Score: 78` の算出根拠なし。
単純平均は弱依存に薄められて鈍感化する。

**決定:** risk 上位 20% エッジ平均（CVaR 的）を repo risk とする（[scoring.md](scoring.md) S2）。

### C3. Volatility 正規化が CI ゲートを不安定化
`log(1+count)/log(1+maxCount)` は repo 相対。無関係ファイルの変動で
全エッジ volatility が動き、`--min-grade B` の絶対バーが揺れる。

**決定:** 絶対スケール化（90日窓・固定基準）。CI ゲートの一次判定は
volatility を含まない構造 risk で行う（[scoring.md](scoring.md) S3）。

### C4. Score と Rule の二重カウント
「禁止越境 → distance 1.0」と `LayerViolationRule(error)` が同一事象を二重評価。

**決定:** 構造違反は Rule（離散・絶対）、distance は連続スコアの素材に役割分離。
distance は越境で 1.0 に張り付けない（[scoring.md](scoring.md) S4）。

---

## 🟠 Medium

### M1. エッジ多重度・集約ポリシー未定義
同一 (source,target) に field / methodCall / objectCreation が併存。
`SourceLine/Evidence` 単一では多出現を表せない。

**決定:** Model に `Occurrences[] {kind,file,line}` を持たせ、
エッジ strength = `max(kind strength)`（[implementation-plan.md](implementation-plan.md) Model 節）。

### M2. syntax-only の忠実度が楽観的
semantic なしでは `new Foo()` の Foo を解決できず distance が決定不能。

**決定:** syntax-only は **Risk 非提供**。依存一覧 + 循環検出 + using グラフまで。
`Confidence: low` を明示。

### M3. レイヤー判定が project 名依存
モジュラーモノリス（単一 project・namespace 分割）に対応不可。

**決定:** layer `patterns` に namespace パターンも許可。

### M4. DI/Reflection の strength レンジに決定規則なし
`DiRegistration 0.30–0.80` の幅内選択規則が未定義。§12 本文(0.50) と表が不一致。

**決定:** 解決可能性で段階化する決定表を定義（[scoring.md](scoring.md) 付録 A）。

### M5. `--check` のゲート条件未定義
`--diff` 併用時、min-grade のみで落とすか新規違反でも落とすか不明。

**決定:** ゲート条件表を固定（[implementation-plan.md](implementation-plan.md) CI 節）。

---

## 🟢 Minor

- N1: §17 `GodClassRule` に対応する config キーがない → config に追加。
- N2: confidence の合成規則未定義 → low エッジ比率で repo confidence を減衰。
- N3: 性能目標は MSBuildWorkspace 初回 compilation を考えると野心的 → MVP は「数分」と幅を持たせる。
- N4: `MSBuildLocator` 登録 / `WorkspaceFailed` / partial class 跨ぎは既知の地雷 → §26.3 方針（落ちた project は警告継続）を堅持。
