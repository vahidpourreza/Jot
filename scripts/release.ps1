[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')][string]$Version,
    [string]$InnoCompiler,
    [string]$RuntimeCab,
    [switch]$Verify,
    [switch]$TestInstaller,
    [switch]$NoRestore
)

$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$jotRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$jotArtifacts=Join-Path $jotRoot 'artifacts'
$jotCache=Join-Path $jotArtifacts 'cache'
$jotPublish=Join-Path $jotArtifacts 'publish\win-x64'
$jotRelease=Join-Path $jotRoot 'dist\installer'
$jotProject=Join-Path $jotRoot 'Jot.csproj'
$jotClock=[Diagnostics.Stopwatch]::StartNew()

function Assert-JotChild([string]$Path,[string]$Parent) {
    $full=[IO.Path]::GetFullPath($Path)
    $base=[IO.Path]::GetFullPath($Parent).TrimEnd('\')+'\'
    if(!$full.StartsWith($base,[StringComparison]::OrdinalIgnoreCase)){throw "Path is outside the build area: $full"}
    $ancestor=$full
    while($ancestor -and $ancestor.Length -ge $base.TrimEnd('\').Length){
        if((Test-Path -LiteralPath $ancestor) -and (Get-Item -LiteralPath $ancestor).LinkType){throw "Refusing a linked build path: $ancestor"}
        $ancestor=Split-Path $ancestor -Parent
    }
    return $full
}
function Remove-JotBuildItem([string]$Path,[string]$Parent) {
    $safe=Assert-JotChild $Path $Parent
    if(Test-Path -LiteralPath $safe){
        if((Get-Item -LiteralPath $safe).PSIsContainer -and @(Get-ChildItem -LiteralPath $safe -Recurse -Force -Attributes ReparsePoint).Count){throw "Refusing cleanup through a linked child: $safe"}
        Remove-Item -LiteralPath $safe -Recurse -Force
    }
}
function Invoke-JotTool([string]$File,[string[]]$Arguments,[switch]$Quiet) {
    if($Quiet){$output=& $File @Arguments 2>&1}else{& $File @Arguments}
    if($LASTEXITCODE -ne 0){if($Quiet){$output|Select-Object -Last 15|Write-Host};throw "$File failed with exit code $LASTEXITCODE"}
}
function Invoke-JotProcess([string]$File,[string[]]$Arguments,[int]$TimeoutSeconds=90,[switch]$IsolateRuntimes) {
    $info=[Diagnostics.ProcessStartInfo]::new($File)
    $info.UseShellExecute=$false;$info.CreateNoWindow=$true;$info.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    foreach($argument in $Arguments){$info.ArgumentList.Add($argument)}
    if($IsolateRuntimes){
        $missing=Join-Path $jotArtifacts 'intentionally-missing-runtime'
        $info.Environment['DOTNET_ROOT']=$missing;$info.Environment['DOTNET_ROOT_X64']=$missing
        $info.Environment['DOTNET_MULTILEVEL_LOOKUP']='0'
        $info.Environment['WEBVIEW2_BROWSER_EXECUTABLE_FOLDER']=$missing
    }
    $child=[Diagnostics.Process]::Start($info)
    try{
        if(!$child.WaitForExit($TimeoutSeconds*1000)){$child.Kill();throw "Timed out: $File"}
        if($child.ExitCode -ne 0){throw "$File failed with exit code $($child.ExitCode). Inspect the isolated test logs."}
    } finally {$child.Dispose()}
}
function Assert-JotChecks([string]$Report,[int]$Minimum=1) {
    if(!(Test-Path -LiteralPath $Report)){throw "Missing validation report: $Report"}
    $results=@(Get-Content -LiteralPath $Report -Raw|ConvertFrom-Json)
    if($results.Count -lt $Minimum){throw "Validation report is incomplete: $Report"}
    $failed=@($results|Where-Object{$_.passed -isnot [bool] -or -not $_.passed})
    if($failed.Count){throw ('Validation failed: '+(($failed|ForEach-Object name)-join ', '))}
    Write-Host "$($results.Count) checks passed: $Report"
}
function Get-JotRuntimeFiles([string]$Directory) {
    if(@(Get-ChildItem -LiteralPath $Directory -Recurse -Force -Attributes ReparsePoint).Count){throw 'Runtime contains linked paths.'}
    @(Get-ChildItem -LiteralPath $Directory -File -Recurse -Force|Sort-Object FullName|ForEach-Object{
        [pscustomobject]@{path=$_.FullName.Substring($Directory.TrimEnd('\').Length+1);length=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
    })
}
function Test-JotRuntimeFiles([string]$Directory,$Files) {
    if(!(Test-Path -LiteralPath $Directory) -or !$Files -or (Get-Item -LiteralPath $Directory).LinkType){return $false}
    if(@(Get-ChildItem -LiteralPath $Directory -Recurse -Force -Attributes ReparsePoint).Count){throw 'Runtime contains linked paths.'}
    if(@(Get-ChildItem -LiteralPath $Directory -File -Recurse -Force).Count -ne @($Files).Count){return $false}
    foreach($file in $Files){
        $path=Assert-JotChild (Join-Path $Directory $file.path) $Directory
        if(!(Test-Path -LiteralPath $path) -or (Get-Item -LiteralPath $path).Length -ne $file.length -or (Get-FileHash -LiteralPath $path).Hash -ne $file.sha256){return $false}
    }
    return $true
}

if($PSVersionTable.PSVersion.Major -lt 7){throw 'Build with PowerShell 7. End users do not need PowerShell.'}
if(!(Get-Command dotnet -ErrorAction SilentlyContinue)){throw '.NET 10 SDK is needed on the developer machine only.'}
if(!$InnoCompiler){
    $candidates=@((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),(Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))
    $InnoCompiler=$candidates|Where-Object{Test-Path -LiteralPath $_}|Select-Object -First 1
}
if(!$InnoCompiler -or !(Test-Path -LiteralPath $InnoCompiler)){throw 'Install Inno Setup 6 on the developer machine or pass -InnoCompiler. Nothing is installed globally by this script.'}
if(!$Version){[xml]$project=Get-Content -LiteralPath $jotProject;$Version=[string]($project.Project.PropertyGroup|Where-Object Version|Select-Object -First 1).Version}
if($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$'){throw 'Use a numeric release version such as 1.5.0.'}
$runtimeManifest=Get-Content -LiteralPath (Join-Path $jotRoot 'packaging\webview2-runtime.json') -Raw|ConvertFrom-Json
$initialRuntimePin=[string]::IsNullOrWhiteSpace($runtimeManifest.sha256)
if($runtimeManifest.architecture -ne 'x64' -or $runtimeManifest.version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or (!$initialRuntimePin -and $runtimeManifest.sha256 -notmatch '^[A-Fa-f0-9]{64}$')){throw 'The pinned WebView2 manifest is invalid.'}
$runtimeUri=[Uri]$runtimeManifest.url
if($runtimeUri.Scheme -ne 'https' -or $runtimeUri.Host -ne 'msedge.sf.dl.delivery.mp.microsoft.com'){throw 'Runtime downloads must use the pinned Microsoft HTTPS source.'}
foreach($directory in @($jotArtifacts,$jotCache,$jotRelease)){New-Item -ItemType Directory -Path $directory -Force|Out-Null}
$buildLock=[IO.File]::Open((Join-Path $jotArtifacts 'release.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
Push-Location $jotRoot
try {
    $cab=Join-Path $jotCache ('downloads\Microsoft.WebView2.FixedVersionRuntime.'+$runtimeManifest.version+'.x64.cab')
    $runtimeCache=Assert-JotChild (Join-Path $jotCache ('webview2\'+$runtimeManifest.version)) $jotArtifacts
    if($RuntimeCab){
        $provided=Get-Item -LiteralPath $RuntimeCab
        if($provided.PSIsContainer -or $provided.Extension -ne '.cab'){throw 'RuntimeCab must be the official x64 WebView2 CAB file.'}
        if(!$initialRuntimePin -and (Get-FileHash -LiteralPath $provided.FullName).Hash -ne $runtimeManifest.sha256){throw 'Provided CAB does not match the pinned checksum.'}
        New-Item -ItemType Directory -Path (Split-Path $cab) -Force|Out-Null
        if($provided.FullName -ne $cab){Copy-Item -LiteralPath $provided.FullName -Destination $cab -Force}
    }
    if(!(Test-Path -LiteralPath $cab)){
        New-Item -ItemType Directory -Path (Split-Path $cab) -Force|Out-Null
        $partial=$cab+'.partial';Write-Host 'Downloading the pinned WebView2 runtime (cached for subsequent releases)...'
        $handler=[Net.Http.SocketsHttpHandler]::new();$handler.ConnectTimeout=[TimeSpan]::FromSeconds(15)
        $client=[Net.Http.HttpClient]::new($handler);$client.Timeout=[TimeSpan]::FromMinutes(10)
        try{
            $inputStream=$client.GetStreamAsync([string]$runtimeManifest.url).GetAwaiter().GetResult()
            try{$outputStream=[IO.File]::Create($partial);try{$inputStream.CopyTo($outputStream)}finally{$outputStream.Dispose()}}finally{$inputStream.Dispose()}
        }catch{throw 'Could not download WebView2. Obtain the pinned x64 CAB from Microsoft and rerun with -RuntimeCab <path>; no installer was produced.'}finally{$client.Dispose()}
        if(!$initialRuntimePin -and (Get-FileHash -LiteralPath $partial).Hash -ne $runtimeManifest.sha256){throw 'WebView2 download checksum mismatch.'}
        Move-Item -LiteralPath $partial -Destination $cab
    }
    $actualRuntimeHash=(Get-FileHash -LiteralPath $cab).Hash
    if(!$initialRuntimePin -and $actualRuntimeHash -ne $runtimeManifest.sha256){throw 'Cached runtime checksum mismatch; do not use this package.'}
    $runtimeProofPath=$runtimeCache+'.verified.json';$runtimeProof=$null
    if(Test-Path -LiteralPath $runtimeProofPath){try{$runtimeProof=Get-Content -LiteralPath $runtimeProofPath -Raw|ConvertFrom-Json}catch{$runtimeProof=$null}}
    $cacheValid=$runtimeProof -and $runtimeProof.cabSha256 -eq $actualRuntimeHash -and (Test-JotRuntimeFiles $runtimeCache $runtimeProof.files)
    if(!$cacheValid){
        Remove-JotBuildItem $runtimeCache $jotArtifacts
        $extract=Assert-JotChild (Join-Path $jotArtifacts ('extract-'+[Guid]::NewGuid().ToString('N'))) $jotArtifacts
        New-Item -ItemType Directory -Path $extract|Out-Null
        Write-Host 'Extracting the runtime into the local build cache...'
        Invoke-JotTool (Join-Path $env:SystemRoot 'System32\expand.exe') @($cab,'-F:*',$extract) -Quiet
        $executables=@(Get-ChildItem -LiteralPath $extract -Filter msedgewebview2.exe -Recurse -File)
        if($executables.Count -ne 1){throw 'The runtime archive did not contain exactly one browser executable.'}
        New-Item -ItemType Directory -Path (Split-Path $runtimeCache) -Force|Out-Null
        Move-Item -LiteralPath $executables[0].Directory.FullName -Destination $runtimeCache
        if((Test-Path -LiteralPath $extract) -and !(Get-ChildItem -LiteralPath $extract -Force)){Remove-Item -LiteralPath $extract}
    }
    $browserFile=Join-Path $runtimeCache 'msedgewebview2.exe'
    if((Get-Item -LiteralPath $browserFile).VersionInfo.ProductVersion -ne $runtimeManifest.version){throw 'The extracted runtime has an unexpected version.'}
    if($initialRuntimePin -or !$cacheValid){
        $signature=Get-AuthenticodeSignature -LiteralPath $browserFile
        if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Microsoft Corporation'){throw 'The WebView2 executable signature could not be verified.'}
        $runtimeProof=[ordered]@{cabSha256=$actualRuntimeHash;files=@(Get-JotRuntimeFiles $runtimeCache)}
        [IO.File]::WriteAllText($runtimeProofPath,($runtimeProof|ConvertTo-Json -Depth 5))
    }
    if($initialRuntimePin){
        $runtimeManifest.sha256=$actualRuntimeHash
        [IO.File]::WriteAllText((Join-Path $jotRoot 'packaging\webview2-runtime.json'),($runtimeManifest|ConvertTo-Json))
        Write-Host 'Pinned the verified Microsoft runtime SHA-256 in packaging/webview2-runtime.json. Keep this manifest in source control.'
    }
    $jotPublish=Assert-JotChild $jotPublish $jotArtifacts
    $usingStage=@(Get-CimInstance Win32_Process|Where-Object{$_.ExecutablePath -and $_.ExecutablePath.StartsWith($jotPublish+'\',[StringComparison]::OrdinalIgnoreCase)})
    if($usingStage.Count){throw 'The previous packaging-stage app is running. Close it before rebuilding.'}
    # Retain the expensive runtime copy between releases; regenerate app output separately.
    $appOutput=Assert-JotChild (Join-Path $jotArtifacts 'app-output') $jotArtifacts
    Remove-JotBuildItem $appOutput $jotArtifacts
    if(!$NoRestore){Invoke-JotTool 'dotnet' @('restore',$jotProject,'-r','win-x64','--locked-mode','--source','https://api.nuget.org/v3/index.json')}
    Invoke-JotTool 'dotnet' @('publish',$jotProject,'-c','Release','-r','win-x64','--self-contained','true','--no-restore','-p:JotOfflinePackage=true',('-p:Version='+$Version),'-p:DebugType=None','-p:DebugSymbols=false','-o',$appOutput)
    New-Item -ItemType Directory -Path $jotPublish -Force|Out-Null
    foreach($old in @(Get-ChildItem -LiteralPath $jotPublish -Force|Where-Object Name -ne 'WebView2Runtime')){
        Remove-JotBuildItem $old.FullName $jotPublish
    }
    Get-ChildItem -LiteralPath $appOutput -Force|Copy-Item -Destination $jotPublish -Recurse -Force
    $runtimeTarget=Assert-JotChild (Join-Path $jotPublish 'WebView2Runtime') $jotPublish
    if(!(Test-JotRuntimeFiles $runtimeTarget $runtimeProof.files)){
        Remove-JotBuildItem $runtimeTarget $jotPublish
        Copy-Item -LiteralPath $runtimeCache -Destination $runtimeTarget -Recurse
    }
    Invoke-JotTool (Join-Path $env:SystemRoot 'System32\icacls.exe') @($runtimeTarget,'/grant','*S-1-15-2-2:(OI)(CI)(RX)','*S-1-15-2-1:(OI)(CI)(RX)','/T','/Q') -Quiet
    $package=[ordered]@{appVersion=$Version;rid='win-x64';webView2Version=$runtimeManifest.version;webView2CabSha256=$runtimeManifest.sha256;offline=$true}
    [IO.File]::WriteAllText((Join-Path $jotPublish 'jot-package.json'),($package|ConvertTo-Json))
    $runtimeConfig=Get-Content -LiteralPath (Join-Path $jotPublish 'Jot.runtimeconfig.json') -Raw|ConvertFrom-Json
    if($runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'framework' -or $runtimeConfig.runtimeOptions.PSObject.Properties.Name -contains 'frameworks'){throw 'Publish is framework-dependent, not self-contained.'}
    foreach($required in @('Jot.exe','hostfxr.dll','hostpolicy.dll','coreclr.dll','System.Private.CoreLib.dll','PresentationFramework.dll','e_sqlite3.dll','runtimes\win-x64\native\WebView2Loader.dll')){
        if(!(Test-Path -LiteralPath (Join-Path $jotPublish $required))){throw "Missing offline payload file: $required"}
    }
    # Preserve redistribution notices from the actual restored package versions.
    $licenses=Join-Path $jotPublish 'licenses';New-Item -ItemType Directory -Path $licenses -Force|Out-Null
    Get-ChildItem -LiteralPath (Join-Path $jotRoot 'packaging\licenses') -File|Copy-Item -Destination $licenses
    Copy-Item -LiteralPath (Join-Path $jotRoot 'packaging\THIRD-PARTY-NOTICES.txt') -Destination $jotPublish
    $assets=Get-Content -LiteralPath (Join-Path $jotRoot 'obj\project.assets.json') -Raw|ConvertFrom-Json
    $packageIds=@($assets.libraries.PSObject.Properties.Name)
    foreach($framework in $runtimeConfig.runtimeOptions.includedFrameworks){$packageIds+=($framework.name.ToLowerInvariant()+'.runtime.win-x64/'+$framework.version)}
    foreach($packageId in @($packageIds|Sort-Object -Unique)){
        foreach($packageRoot in $assets.packageFolders.PSObject.Properties.Name){
            $packageFolder=Join-Path $packageRoot $packageId.ToLowerInvariant()
            if(Test-Path -LiteralPath $packageFolder){
                $noticeFiles=@(Get-ChildItem -LiteralPath $packageFolder -File|Where-Object {$_.Name -match '^(LICENSE|NOTICE|THIRD.PARTY.NOTICES)' -or $_.Extension -eq '.nuspec'})
                if($noticeFiles.Count){$destination=Join-Path $licenses ($packageId.Replace('/','-'));New-Item -ItemType Directory -Path $destination -Force|Out-Null;$noticeFiles|Copy-Item -Destination $destination}
                break
            }
        }
    }
    $innoLicense=Join-Path (Split-Path $InnoCompiler) 'license.txt'
    if(Test-Path -LiteralPath $innoLicense){$destination=Join-Path $licenses 'inno-setup';New-Item -ItemType Directory -Path $destination -Force|Out-Null;Copy-Item -LiteralPath $innoLicense -Destination $destination}
    $verification=Join-Path $jotArtifacts ('verification\'+$Version+'-'+[DateTime]::Now.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
    Invoke-JotProcess (Join-Path $jotPublish 'Jot.exe') @('--package-smoke','--test-output',(Join-Path $verification 'smoke')) -IsolateRuntimes
    Assert-JotChecks (Join-Path $verification 'smoke\package-smoke.json') -Minimum 8
    if($Verify){Invoke-JotProcess (Join-Path $jotPublish 'Jot.exe') @('--self-test','--test-output',(Join-Path $verification 'full')) -TimeoutSeconds 300 -IsolateRuntimes;Assert-JotChecks (Join-Path $verification 'full\results.json')}
    if($TestInstaller){
        & (Join-Path $PSScriptRoot 'test-installer.ps1') -Version $Version -PublishDir $jotPublish -Compiler $InnoCompiler -OutputRoot (Join-Path $verification 'installer')
    }
    Write-Host 'Compiling the single offline installer...'
    Invoke-JotTool $InnoCompiler @('/Q',('/DAppVersion='+$Version),('/DPublishDir='+$jotPublish),('/DReleaseDir='+$jotRelease),(Join-Path $jotRoot 'packaging\jot.iss'))
    $installer=Join-Path $jotRelease ('Jot-Setup-'+$Version+'-win-x64.exe')
    $hash=(Get-FileHash -LiteralPath $installer).Hash
    [IO.File]::WriteAllText(($installer+'.sha256'),$hash+'  '+[IO.Path]::GetFileName($installer)+[Environment]::NewLine)
    $details=[ordered]@{version=$Version;file=[IO.Path]::GetFileName($installer);sha256=$hash;sizeMiB=[math]::Round((Get-Item -LiteralPath $installer).Length/1MB,2);webView2Version=$runtimeManifest.version;elapsedSeconds=[math]::Round($jotClock.Elapsed.TotalSeconds,1);verification=$verification;fullSuite=[bool]$Verify;installerTest=[bool]$TestInstaller}
    [IO.File]::WriteAllText((Join-Path $jotRelease 'release-info.json'),($details|ConvertTo-Json))
    Write-Host ('Ready: '+$installer)
    $details|ConvertTo-Json
} finally {Pop-Location;$buildLock.Dispose()}
