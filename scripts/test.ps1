param([string]$Configuration = 'Debug')

$ErrorActionPreference = 'Stop'
dotnet test (Join-Path $PSScriptRoot '..\NTech.DataStudio.slnx') -c $Configuration
exit $LASTEXITCODE
