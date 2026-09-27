[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$PublishDir,
    [Parameter(Mandatory)][string]$Compiler,
    [Parameter(Mandatory)][string]$OutputRoot
)
$ErrorActionPreference='Stop'
$jotRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$jotTestRoot=[IO.Path]::GetFullPath($OutputRoot)
if(!$jotTestRoot.StartsWith((Join-Path $jotRoot 'artifacts\verification\'),[StringComparison]::OrdinalIgnoreCase)){throw 'Installer validation must stay inside artifacts/verification.'}
if(Test-Path -LiteralPath $jotTestRoot){throw 'Use a new, empty installer test directory.'}
New-Item -ItemType Directory -Path $jotTestRoot|Out-Null
$jotInstall=Join-Path $jotTestRoot 'installed-app'
$jotData=Join-Path $jotTestRoot 'isolated-data'
New-Item -ItemType Directory -Path $jotData|Out-Null
$jotSentinel=Join-Path $jotData 'notes-sentinel.txt'
[IO.File]::WriteAllText($jotSentinel,'Installer must preserve user data.')
$jotDataHash=(Get-FileHash -LiteralPath $jotSentinel).Hash
$jotChecks=[Collections.Generic.List[object]]::new()
function Invoke-JotInstallerProcess([string]$File,[string[]]$Arguments,[switch]$PrivateRuntime) {
    $info=[Diagnostics.ProcessStartInfo]::new($File);$info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    if($PrivateRuntime){$absent=Join-Path $jotTestRoot 'missing-runtime';$info.Environment['DOTNET_ROOT']=$absent;$info.Environment['DOTNET_ROOT_X64']=$absent;$info.Environment['DOTNET_MULTILEVEL_LOOKUP']='0';$info.Environment['WEBVIEW2_BROWSER_EXECUTABLE_FOLDER']=$absent}
    $process=[Diagnostics.Process]::Start($info)
    try{if(!$process.WaitForExit(120000)){$process.Kill();throw 'Owned installer-test process timed out.'};if($process.ExitCode -ne 0){throw "Installer test process failed: $($process.ExitCode)"}}finally{$process.Dispose()}
}
& $Compiler '/Q' '/DValidationBuild=1' ('/DAppVersion='+$Version) ('/DPublishDir='+$PublishDir) ('/DReleaseDir='+$jotTestRoot) '/FJot-Validation' (Join-Path $jotRoot 'packaging\jot.iss')
if($LASTEXITCODE -ne 0){throw 'Validation installer compile failed.'}
$jotSetup=Join-Path $jotTestRoot 'Jot-Validation.exe'
$jotCommon=@('/VERYSILENT','/SUPPRESSMSGBOXES','/SP-','/NORESTART','/NOICONS','/NOCLOSEAPPLICATIONS','/NORESTARTAPPLICATIONS',('/DIR='+$jotInstall))
Invoke-JotInstallerProcess $jotSetup ($jotCommon+@('/LOG='+(Join-Path $jotTestRoot 'install.log')))
$jotChecks.Add(@{name='installer-extracts-app-and-private-runtime';passed=(Test-Path -LiteralPath (Join-Path $jotInstall 'Jot.exe')) -and (Test-Path -LiteralPath (Join-Path $jotInstall 'WebView2Runtime\msedgewebview2.exe'))})
$jotChecks.Add(@{name='installer-excludes-development-test-assets';passed=!(Test-Path -LiteralPath (Join-Path $jotInstall 'tests\renderer-tests.js'))})
$jotChecks.Add(@{name='validation-does-not-register-uninstaller';passed=!(Test-Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{5730A854-61C8-43C2-9834-25C4C46CC086}_is1')})
Invoke-JotInstallerProcess (Join-Path $jotInstall 'Jot.exe') @('--package-smoke','--test-output',(Join-Path $jotData 'smoke')) -PrivateRuntime
$smoke=Get-Content -LiteralPath (Join-Path $jotData 'smoke\package-smoke.json') -Raw|ConvertFrom-Json
$jotChecks.Add(@{name='installed-app-runs-with-bundled-runtimes';passed=@($smoke|Where-Object{-not $_.passed}).Count -eq 0 -and $smoke.Count -ge 8})
# Reinstall the same version to test repair/upgrade replacement without launching the UI.
$jotRepairTarget=[IO.Path]::GetFullPath((Join-Path $jotInstall 'shared.js'))
if(!$jotRepairTarget.StartsWith($jotInstall+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe repair fixture path.'}
Remove-Item -LiteralPath $jotRepairTarget
Invoke-JotInstallerProcess $jotSetup ($jotCommon+@('/LOG='+(Join-Path $jotTestRoot 'repair.log')))
$jotChecks.Add(@{name='reinstall-repairs-owned-files';passed=(Get-FileHash -LiteralPath $jotRepairTarget).Hash -eq (Get-FileHash -LiteralPath (Join-Path $PublishDir 'shared.js')).Hash})
$jotChecks.Add(@{name='reinstall-preserves-data';passed=(Get-FileHash -LiteralPath $jotSentinel).Hash -eq $jotDataHash})
$jotDatabase=Join-Path $jotData 'smoke\data\jot.db'
$jotDatabaseHash=(Get-FileHash -LiteralPath $jotDatabase).Hash
Invoke-JotInstallerProcess (Join-Path $jotInstall 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG='+(Join-Path $jotTestRoot 'uninstall.log')))
for($jotWait=0;$jotWait -lt 100 -and (Test-Path -LiteralPath (Join-Path $jotInstall 'Jot.exe'));$jotWait++){Start-Sleep -Milliseconds 100}
$jotChecks.Add(@{name='uninstall-removes-app';passed=!(Test-Path -LiteralPath (Join-Path $jotInstall 'Jot.exe'))})
$jotChecks.Add(@{name='uninstall-removes-private-runtime';passed=!(Test-Path -LiteralPath (Join-Path $jotInstall 'WebView2Runtime\msedgewebview2.exe'))})
$jotChecks.Add(@{name='uninstall-preserves-notes-and-sentinel';passed=(Get-FileHash -LiteralPath $jotDatabase).Hash -eq $jotDatabaseHash -and (Get-FileHash -LiteralPath $jotSentinel).Hash -eq $jotDataHash})
[IO.File]::WriteAllText((Join-Path $jotTestRoot 'results.json'),($jotChecks|ConvertTo-Json))
if(@($jotChecks|Where-Object{-not $_.passed}).Count){throw 'Installer validation failed; inspect its results.json.'}
Write-Host "$($jotChecks.Count) isolated install/repair/uninstall checks passed."
