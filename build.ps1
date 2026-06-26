#requires -Version 5.1
<#
.SYNOPSIS
    dotnet-coupling のビルド/テスト/発行スクリプト。

.DESCRIPTION
    ツール本体は net472（docs/implementation-plan.md §0）。.NET Framework は
    self-contained 単一ファイル発行ができないため、Costura.Fody でマネージド依存を
    exe に埋め込み、Publish では単体 dotnet-coupling.exe を出力フォルダへコピーする。
    実行には解析対象マシンに .NET Framework 4.7.2 ランタイム、semantic 解析には
    VS / Build Tools の MSBuild が必要（MSBuild は同梱されず実行時に解決される）。
    ダブルクリック実行は build.cmd 経由（.ps1 はダブルクリックでは実行されないため）。

.PARAMETER Task
    実行するタスク: All（既定: Restore→Build→Test→Publish）/ Clean / Restore / Build / Test / Publish

.PARAMETER Configuration
    Debug / Release（既定: Release）

.PARAMETER OutputDir
    Publish の出力フォルダ（既定: dist）

.PARAMETER SkipTests
    All のときテストを省略する

.EXAMPLE
    .\build.ps1                       # Release で一式（restore/build/test/publish）
    .\build.ps1 -Task Test -Configuration Debug
    .\build.ps1 -Task Publish -OutputDir out
    .\build.ps1 -Task Clean
#>
[CmdletBinding()]
param(
    [ValidateSet('All', 'Clean', 'Restore', 'Build', 'Test', 'Publish')]
    [string]$Task = 'All',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$OutputDir = 'dist',

    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$Root        = $PSScriptRoot
$Solution    = Join-Path $Root 'dotnet-coupling.slnx'
$CliProject  = Join-Path $Root 'src\DotnetCoupling.Cli\DotnetCoupling.Cli.csproj'

# 素の関数（param ブロックを置かない）。全トークンを自動変数 $args に渡すことで、
# -o 等が advanced function の共通パラメータ(-OutVariable/-OutBuffer)と衝突するのを避ける。
function Invoke-Dotnet {
    Write-Host ">> dotnet $($args -join ' ')" -ForegroundColor Cyan
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($args -join ' ') が失敗しました (exit $LASTEXITCODE)"
    }
}

function Invoke-Clean {
    Write-Host '== Clean ==' -ForegroundColor Yellow
    Get-ChildItem -Path (Join-Path $Root 'src'), (Join-Path $Root 'tests') -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in 'bin', 'obj' } |
        ForEach-Object {
            Write-Host "  remove $($_.FullName)"
            Remove-Item -Recurse -Force $_.FullName
        }
    foreach ($dir in @($OutputDir, 'TestResults')) {
        $path = Join-Path $Root $dir
        if (Test-Path $path) {
            Write-Host "  remove $path"
            Remove-Item -Recurse -Force $path
        }
    }
}

function Invoke-Restore {
    Write-Host '== Restore ==' -ForegroundColor Yellow
    Invoke-Dotnet restore $Solution
}

function Invoke-Build {
    Write-Host "== Build ($Configuration) ==" -ForegroundColor Yellow
    Invoke-Dotnet build $Solution -c $Configuration --nologo
}

function Invoke-Test {
    Write-Host "== Test ($Configuration) ==" -ForegroundColor Yellow
    Invoke-Dotnet test $Solution -c $Configuration --nologo
}

function Invoke-Publish {
    # Costura が依存を埋め込んだ単体 exe は bin にできる。dotnet publish は依存を
    # 再コピーして単体性を壊すため使わず、bin の exe を出力フォルダへコピーするだけにする。
    Write-Host "== Publish ($Configuration) -> $OutputDir ==" -ForegroundColor Yellow
    $out = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $Root $OutputDir }
    $binExe = Join-Path $Root ("src\DotnetCoupling.Cli\bin\{0}\net472\dotnet-coupling.exe" -f $Configuration)
    if (-not (Test-Path $binExe)) { throw "exe が見つかりません: $binExe（先に Build を実行してください）" }

    if (Test-Path $out) { Remove-Item -Recurse -Force $out }
    New-Item -ItemType Directory -Path $out | Out-Null
    $exe = Join-Path $out 'dotnet-coupling.exe'
    Copy-Item $binExe $exe -Force

    Write-Host ''
    Write-Host "Published single exe: $exe" -ForegroundColor Green
    Write-Host ("  size: {0:n0} bytes（マネージド依存を Costura で埋め込み済み）" -f (Get-Item $exe).Length) -ForegroundColor DarkGray
    Write-Host '  実行には解析対象マシンに .NET Framework 4.7.2 ランタイムが必要。' -ForegroundColor DarkGray
    Write-Host '  semantic 解析は VS/Build Tools の MSBuild、Git volatility はネイティブ git2 を要する（未検証）。' -ForegroundColor DarkGray
    Write-Host ("  実行例: .\{0}\dotnet-coupling.exe MyApp.sln --summary" -f $OutputDir) -ForegroundColor DarkGray
}

$start = Get-Date
Write-Host "dotnet-coupling build | Task=$Task Configuration=$Configuration" -ForegroundColor White

switch ($Task) {
    'Clean'   { Invoke-Clean }
    'Restore' { Invoke-Restore }
    'Build'   { Invoke-Restore; Invoke-Build }
    'Test'    { Invoke-Restore; Invoke-Build; Invoke-Test }
    'Publish' { Invoke-Restore; Invoke-Build; Invoke-Publish }
    'All' {
        Invoke-Restore
        Invoke-Build
        if (-not $SkipTests) { Invoke-Test } else { Write-Host '== Test skipped ==' -ForegroundColor DarkGray }
        Invoke-Publish
    }
}

$elapsed = (Get-Date) - $start
Write-Host ("Done in {0:n1}s" -f $elapsed.TotalSeconds) -ForegroundColor Green





