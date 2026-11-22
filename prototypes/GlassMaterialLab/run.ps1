$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'

# 任意参数转发给应用，例如 --software 以软件渲染启动（检验玻璃回退路径）。
dotnet run --project $PSScriptRoot\GlassMaterialLab.csproj -- @args
