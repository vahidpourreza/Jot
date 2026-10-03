[CmdletBinding()]
param([switch]$NoRestore)
$ErrorActionPreference = 'Stop'
$jotProject = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$jotArgs = @('build', (Join-Path $jotProject 'Jot.csproj'), '-c', 'Release', '-r', 'win-x64')
if ($NoRestore) { $jotArgs += '--no-restore' }
& dotnet @jotArgs
if ($LASTEXITCODE -ne 0) { throw 'File icon build failed.' }
$jotPacker = Join-Path $jotProject 'bin\Release\net10.0-windows10.0.19041.0\win-x64\Jot.exe'
$jotTarget = Join-Path $jotProject 'assets\jot-file.ico'
$jotExport = Start-Process -FilePath $jotPacker -ArgumentList @('--export-file-icon', ('"' + $jotTarget + '"')) -WindowStyle Hidden -Wait -PassThru
if ($jotExport.ExitCode -ne 0) { throw 'File icon export failed.' }
Get-Item -LiteralPath $jotTarget | Select-Object FullName,Length
