$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$projectPath = Join-Path $repoRoot 'src/AgySwitch/AgySwitch.csproj'
$publishPath = Join-Path $repoRoot 'artifacts/installer-publish'
$scriptPath = Join-Path $repoRoot 'installer/AgySwitch.iss'
$setupPath = Join-Path $repoRoot 'dist/AgySwitch-Setup-1.0.0-win-x64.exe'

$compilerCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs/Inno Setup 6/ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe')
)
$compilerPath = $compilerCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $compilerPath) {
    throw '未找到 Inno Setup 6 编译器。请先安装 Inno Setup 6，再运行此脚本。'
}

New-Item -ItemType Directory -Force -Path $publishPath | Out-Null
dotnet publish $projectPath -c Release -r win-x64 --self-contained true `
    -p:DebugType=None -p:DebugSymbols=false -p:PublishTrimmed=false `
    -o $publishPath
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish 失败，退出码：$LASTEXITCODE"
}

& $compilerPath '/Qp' $scriptPath
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup 编译失败，退出码：$LASTEXITCODE"
}

Write-Host "安装包已生成：$setupPath"
