#Requires -Version 7.0
<#
.SYNOPSIS
    构建 All Mod's Sky，产物输出到 release\publish。

.DESCRIPTION
    与 .github/workflows 里的 CI 步骤保持一致：
      1. dotnet build AllModsSky.slnx -c Release（.slnx 需要 .NET 9+，低版本自动改为直接编项目）
      2. dotnet publish 单文件到 release\publish\AllModsSky.exe

    默认发布「框架依赖」单文件（约 1.2 MB），目标机器需要装 .NET 8 Desktop Runtime。
    加 -SelfContained 则把运行时一起打进去（约 150 MB，双击即用）。

.PARAMETER SelfContained
    打包 .NET 运行时，产物可脱离运行时运行。

.PARAMETER NoPause
    结束后不停留（给终端 / CI 用；默认会等你按回车，方便直接看结果）。

.EXAMPLE
    pwsh -File .\build.ps1

.EXAMPLE
    pwsh -File .\build.ps1 -SelfContained -NoPause
#>
[CmdletBinding()]
param(
    [switch]$SelfContained,
    [switch]$NoPause
)

$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot

$outDir = Join-Path $PSScriptRoot 'release\publish'
$appProj = Join-Path $PSScriptRoot 'src\AllModsSky.App\AllModsSky.App.csproj'
$exe = Join-Path $outDir 'AllModsSky.exe'
$total = [System.Diagnostics.Stopwatch]::StartNew()

function Stop-Build {
    param([string]$Message)

    Write-Host ''
    Write-Host "✕ $Message" -ForegroundColor Red
    if (-not $NoPause) { Read-Host '按回车退出' | Out-Null }
    exit 1
}

function Write-Step {
    param([string]$Text)
    Write-Host ''
    Write-Host "▸ $Text" -ForegroundColor Cyan
}

<#
    .NET 10 SDK 做 net8.0 的“单文件发布”时，仍会去找 8.0.x 的 ILLink targets，
    在只装了 10 的机器上会报 MSB4019。编译继续用 10（能读 .slnx），
    发布则临时切到已安装的 8.0.x SDK；两件事互不影响。
#>
function Invoke-Publish {
    param([string[]]$Arguments)

    if ($sdkMajor -lt 9) {
        & dotnet @Arguments | Out-Host
        return $LASTEXITCODE
    }

    $sdk8Line = (& dotnet --list-sdks | Where-Object { $_ -match '^8\.0\.' } | Select-Object -First 1)
    if (-not $sdk8Line) {
        Write-Host '  未找到 .NET 8 SDK，尝试直接用当前 SDK 发布；若报 MSB4019，请安装 .NET 8 SDK。' -ForegroundColor Yellow
        & dotnet @Arguments | Out-Host
        return $LASTEXITCODE
    }

    $sdk8Version = ($sdk8Line -split '\s+')[0]
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ('allmodssky-sdk8-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp -Force | Out-Null

    try {
        @{ sdk = @{ version = $sdk8Version; rollForward = 'latestPatch' } } |
            ConvertTo-Json -Compress |
            Set-Content -LiteralPath (Join-Path $temp 'global.json') -Encoding utf8

        Push-Location -LiteralPath $temp
        try {
            Write-Host "  发布使用 .NET SDK $sdk8Version（临时 global.json）" -ForegroundColor DarkGray
            & dotnet @Arguments | Out-Host
            return $LASTEXITCODE
        }
        finally {
            Pop-Location
        }
    }
    finally {
        Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "All Mod's Sky · 构建" -ForegroundColor White
Write-Host "  输出目录  $outDir"
Write-Host "  运行方式  $(if ($SelfContained) { '自带运行时（self-contained）' } else { '框架依赖（需 .NET 8 Desktop Runtime）' })"

# ---- 环境检查 -------------------------------------------------------------

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Stop-Build '找不到 dotnet，先装 .NET SDK：https://dotnet.microsoft.com/download'
}

# 目标 exe 正在运行时文件被锁住，清理和覆盖都会失败，先拦一道。
$running = Get-Process -Name 'AllModsSky' -ErrorAction SilentlyContinue
if ($running) {
    Stop-Build "AllModsSky.exe 正在运行（PID $($running[0].Id)），先关掉它再构建——exe 文件被锁着。"
}

$sdkVersion = (& dotnet --version).Trim()
$sdkMajor = [int]($sdkVersion.Split('.')[0])

# ---- 1. 编译 --------------------------------------------------------------

Write-Step "[1/3] 编译（SDK $sdkVersion）"

if ($sdkMajor -ge 9) {
    & dotnet build (Join-Path $PSScriptRoot 'AllModsSky.slnx') -c Release --nologo -v minimal -tl:off
}
else {
    Write-Host "  .NET $sdkMajor 读不了 .slnx，改为直接编译项目" -ForegroundColor Yellow
    & dotnet build $appProj -c Release --nologo -v minimal -tl:off
}

if ($LASTEXITCODE -ne 0) { Stop-Build "编译失败（exit $LASTEXITCODE）" }

# ---- 2. 发布 --------------------------------------------------------------

Write-Step "[2/3] 发布单文件到 $outDir"

if (Test-Path -LiteralPath $outDir) {
    Remove-Item -LiteralPath $outDir -Recurse -Force
}

$publishArgs = [System.Collections.Generic.List[string]]::new()
foreach ($a in @('publish', $appProj, '-c', 'Release', '-r', 'win-x64')) { $publishArgs.Add($a) }

if ($SelfContained) {
    foreach ($a in @('--self-contained', 'true', '-p:IncludeNativeLibrariesForSelfExtract=true')) { $publishArgs.Add($a) }
}
else {
    $publishArgs.Add('--no-self-contained')
}

foreach ($a in @(
        '-p:PublishSingleFile=true'
        '-p:EnableCompressionInSingleFile=false'
        '-p:DebugType=none'
        '-p:PublishReadyToRun=false'
        '-o', $outDir
        '--nologo', '-v', 'minimal', '-tl:off')) {
    $publishArgs.Add($a)
}

$publishExit = Invoke-Publish $publishArgs.ToArray()
if ($publishExit -ne 0) { Stop-Build "发布失败（exit $publishExit）" }

if (-not (Test-Path -LiteralPath $exe)) {
    Stop-Build "发布跑完了，但没找到 $exe"
}

# ---- 3. 收尾 --------------------------------------------------------------

$file = Get-Item -LiteralPath $exe
$total.Stop()

Write-Host ''
Write-Host "✓ 构建完成，用时 $([math]::Round($total.Elapsed.TotalSeconds, 1)) 秒" -ForegroundColor Green
Write-Host "  $($file.FullName)"
Write-Host "  $('{0:N2} MB' -f ($file.Length / 1MB))"

if (-not $SelfContained) {
    Write-Host '  目标机器需要 .NET 8 Desktop Runtime。' -ForegroundColor DarkGray
}

Write-Host ''
Write-Host "运行：& `"$exe`"" -ForegroundColor DarkGray

if (-not $NoPause) { Read-Host '按回车退出' | Out-Null }
exit 0
