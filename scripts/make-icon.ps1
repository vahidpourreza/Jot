[CmdletBinding()]
param([switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$jotProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$jotBuildArgs = @('build', (Join-Path $jotProject 'Jot.csproj'), '-c', 'Release')
if ($NoRestore) { $jotBuildArgs += '--no-restore' }
& dotnet @jotBuildArgs
if ($LASTEXITCODE -ne 0) { throw 'Icon packer build failed.' }
$jotPacker = Join-Path $jotProject 'bin\Release\net10.0-windows10.0.19041.0\Jot.exe'
$jotIcon = Join-Path $jotProject 'assets\jot.ico'
$jotExport = Start-Process -FilePath $jotPacker -ArgumentList @('--export-icon', ('"' + $jotIcon + '"')) -WindowStyle Hidden -Wait -PassThru
if ($jotExport.ExitCode -ne 0) { throw 'Icon export failed.' }
Get-Item -LiteralPath $jotIcon | Select-Object FullName,Length
