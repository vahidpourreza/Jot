[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$KeepBuild,
    [string[]]$KeepAdditionalBuilds = @(),
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
$jotRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$jotPrefix = $jotRoot + '\'
if ((& git -C $jotRoot rev-parse --show-toplevel).Replace('/', '\').TrimEnd('\') -ne $jotRoot) { throw 'Run cleanup only from the Jot Git repository.' }
if ((Get-Item -LiteralPath $jotRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked project roots are not supported.' }

function Assert-JotChild([string]$Path) {
    $absolute = [IO.Path]::GetFullPath($Path)
    if (!$absolute.StartsWith($jotPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe cleanup target: $absolute" }
    $cursor = $absolute
    while ($cursor -ne $jotRoot) {
        if ((Test-Path -LiteralPath $cursor) -and ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked cleanup path: $cursor" }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    return $absolute
}
function Get-JotFiles([string]$Path) {
    $item = Get-Item -LiteralPath $Path -Force
    if (!$item.PSIsContainer) { return ,$item }
    $todo = [Collections.Generic.Stack[IO.DirectoryInfo]]::new(); $todo.Push([IO.DirectoryInfo]::new($item.FullName))
    while ($todo.Count) {
        $dir = $todo.Pop()
        if ($dir.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked directory: $($dir.FullName)" }
        foreach ($file in $dir.GetFiles()) {
            if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Linked file: $($file.FullName)" }
            $file
        }
        foreach ($child in $dir.GetDirectories()) {
            if ($child.Name -eq '.git') { throw "Nested Git repository: $($child.FullName)" }
            $todo.Push($child)
        }
    }
}
$keep = Assert-JotChild ([IO.Path]::GetFullPath($KeepBuild, $jotRoot))
if ([IO.Path]::GetDirectoryName($keep) -ne (Join-Path $jotRoot 'dist') -or !(Test-Path -LiteralPath (Join-Path $keep 'Jot.exe'))) { throw 'KeepBuild must be an existing runnable direct child of dist.' }
$distRoot = Join-Path $jotRoot 'dist'
function Get-JotBuildFromReference([string]$Value) {
    if (!$Value) { return }
    $expanded = [Environment]::ExpandEnvironmentVariables($Value)
    foreach ($match in [regex]::Matches($expanded, '(?i)[a-z]:\\[^"<>|]*?\.(?:exe|ico)')) {
        $file = [IO.Path]::GetFullPath($match.Value)
        if (!$file.StartsWith($distRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { continue }
        $name = $file.Substring($distRoot.Length + 1).Split('\')[0]
        $build = Assert-JotChild (Join-Path $distRoot $name)
        if (Test-Path -LiteralPath $build -PathType Container) { $build }
    }
}
function Get-JotRegisteredBuilds {
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $null = $ids.Add('Jot.Note'); $null = $ids.Add('jot_auto_file')
    $keys = [Collections.Generic.List[string]]::new()
    $keys.Add('Registry::HKEY_CLASSES_ROOT\Applications\Jot.exe\shell\open\command')
    foreach ($extension in @('.jot', '.txt')) {
        $extensionKey = Get-Item -LiteralPath ('Registry::HKEY_CLASSES_ROOT\' + $extension) -ErrorAction SilentlyContinue
        $choice = Get-ItemProperty -LiteralPath ('Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\' + $extension + '\UserChoice') -Name ProgId -ErrorAction SilentlyContinue
        foreach ($id in @($(if ($extensionKey) { $extensionKey.GetValue('') }), $choice.ProgId)) {
            if ($id -is [string] -and $id -match '^[A-Za-z0-9_.-]{1,200}$') { $null = $ids.Add($id) }
        }
        $keys.Add('Registry::HKEY_CLASSES_ROOT\' + $extension + '\DefaultIcon')
    }
    foreach ($id in $ids) {
        $keys.Add('Registry::HKEY_CLASSES_ROOT\' + $id + '\shell\open\command')
        $keys.Add('Registry::HKEY_CLASSES_ROOT\' + $id + '\DefaultIcon')
    }
    foreach ($path in $keys) {
        $key = Get-Item -LiteralPath $path -ErrorAction SilentlyContinue
        if ($key) { Get-JotBuildFromReference ([string]$key.GetValue('')) }
    }
}
$preservedBuilds = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$null = $preservedBuilds.Add($keep)
foreach ($extra in $KeepAdditionalBuilds) {
    $absolute = Assert-JotChild ([IO.Path]::GetFullPath($extra, $jotRoot))
    if ([IO.Path]::GetDirectoryName($absolute) -ne $distRoot -or !(Test-Path -LiteralPath (Join-Path $absolute 'Jot.exe'))) { throw 'Additional kept builds must be runnable direct children of dist.' }
    $null = $preservedBuilds.Add($absolute)
}
foreach ($registered in Get-JotRegisteredBuilds) { $null = $preservedBuilds.Add($registered) }
foreach ($process in Get-CimInstance Win32_Process -Filter "Name = 'Jot.exe'") {
    foreach ($running in Get-JotBuildFromReference $process.ExecutablePath) { $null = $preservedBuilds.Add($running) }
}
$targets = [Collections.Generic.List[string]]::new()
foreach ($dir in Get-ChildItem -LiteralPath (Join-Path $jotRoot 'dist') -Directory -Force) {
    if (!$preservedBuilds.Contains($dir.FullName) -and $dir.Name -ne 'installer') { $targets.Add($dir.FullName) }
}
foreach ($relative in @('bin', 'obj\Debug', 'obj\Release', 'artifacts\app-output', 'artifacts\publish', 'artifacts\verification', 'artifacts\template-validation', 'artifacts\cache\webview2', 'artifacts\cache\downloads\runtime-transfer-probe.bin')) {
    $candidate = Join-Path $jotRoot $relative
    if (Test-Path -LiteralPath $candidate) { $targets.Add($candidate) }
}
$testRoot = Join-Path $jotRoot 'test-results'
if (Test-Path -LiteralPath $testRoot) { foreach ($dir in Get-ChildItem -LiteralPath $testRoot -Directory -Force) { $targets.Add($dir.FullName) } }
$sourcePaths = @(& git -C $jotRoot -c core.quotepath=false ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate source files; cleanup is not safe.' }
$plan = foreach ($path in $targets) {
    $absolute = Assert-JotChild $path
    $relative = [IO.Path]::GetRelativePath($jotRoot, $absolute).Replace('\', '/')
    if ($sourcePaths | Where-Object { $_ -eq $relative -or $_.StartsWith($relative + '/', [StringComparison]::OrdinalIgnoreCase) }) { throw "Source files found under cleanup target: $absolute" }
    $files = @(Get-JotFiles $absolute)
    if (($relative.StartsWith('dist/') -or $relative -eq 'bin' -or $relative.StartsWith('obj/')) -and ($files | Where-Object { $_.Name -like 'notes.json*' -or $_.Name -like 'jot.db*' -or $_.Extension -in '.jot','.db','.sqlite','.sqlite3' -or $_.FullName -match '\.WebView2\\' })) { throw "Possible user data in build output; preserve it before cleanup: $absolute" }
    [pscustomobject]@{ Path=$absolute; Bytes=[long](($files | Measure-Object Length -Sum).Sum); Files=$files.Count }
}
if (!$Apply) { Write-Host ('Protected builds: ' + (($preservedBuilds | Sort-Object) -join ', ')); $plan | Select-Object Path,Files,@{n='MiB';e={[math]::Round($_.Bytes/1MB,1)}}; return }

# Keep the expensive compressed download and validate it before removing any
# expanded runtime copies. The next installer build can extract it offline.
$manifest = Get-Content -LiteralPath (Join-Path $jotRoot 'packaging\webview2-runtime.json') -Raw | ConvertFrom-Json
if ($manifest.version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $manifest.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid pinned runtime manifest.' }
$cab = Assert-JotChild (Join-Path $jotRoot ('artifacts\cache\downloads\Microsoft.WebView2.FixedVersionRuntime.' + $manifest.version + '.x64.cab'))
if ((Get-FileHash -LiteralPath $cab -Algorithm SHA256).Hash -ne $manifest.sha256) { throw 'Keep a verified runtime CAB before removing expanded copies.' }
$lockPath = Assert-JotChild (Join-Path $jotRoot 'artifacts\release.lock')
$releaseLock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
try {
    $processes = @(Get-CimInstance Win32_Process)
    $registeredNow = @(Get-JotRegisteredBuilds)
    foreach ($target in $plan) {
        if ($registeredNow | Where-Object { $_ -eq $target.Path -or $_.StartsWith($target.Path + '\', [StringComparison]::OrdinalIgnoreCase) }) { throw "Windows now references $($target.Path); nothing has been deleted." }
        if ($processes | Where-Object { ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($target.Path + '\', [StringComparison]::OrdinalIgnoreCase)) -or ($_.Name -eq 'msedgewebview2.exe' -and $_.CommandLine -and $_.CommandLine.Contains($target.Path, [StringComparison]::OrdinalIgnoreCase)) }) { throw "A process still uses $($target.Path); nothing has been deleted." }
    }
    # Fingerprint source (including uncommitted/untracked files), current build,
    # installer and recovery archives. Do not inspect the actual user note store.
    $protected = @($sourcePaths | ForEach-Object { Join-Path $jotRoot $_ })
    foreach ($path in @($preservedBuilds) + @((Join-Path $jotRoot 'dist\installer'), (Join-Path $jotRoot 'recovery'), (Join-Path $jotRoot 'artifacts\file-association-20261003'), (Join-Path $jotRoot 'artifacts\native-input-diagnostics'))) {
        if (Test-Path -LiteralPath $path) { $protected += @(Get-JotFiles $path | ForEach-Object FullName) }
    }
    if (Test-Path -LiteralPath $testRoot) { $protected += @(Get-ChildItem -LiteralPath $testRoot -File -Filter '*.zip' | ForEach-Object FullName) }
    $protected += $cab
    $fingerprints = @{}; foreach ($path in $protected) { $fingerprints[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $evidence = [Collections.Generic.List[IO.FileInfo]]::new()
    foreach ($base in @($testRoot, (Join-Path $jotRoot 'artifacts\verification'))) {
        if (!(Test-Path -LiteralPath $base)) { continue }
        foreach ($file in Get-JotFiles $base) {
            if ($file.FullName -notmatch '\\(data|webview|[^\\]*\.WebView2)\\' -and ($file.Extension -eq '.png' -or ($file.Extension -in '.json','.txt','.log' -and $file.Name -notlike 'notes.json*'))) { $evidence.Add($file) }
        }
    }
    [IO.Directory]::CreateDirectory((Assert-JotChild $testRoot)) | Out-Null
    $archive = Assert-JotChild (Join-Path $testRoot ('verification-evidence-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.zip'))
    $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
    try { foreach ($file in $evidence) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, [IO.Path]::GetRelativePath($jotRoot,$file.FullName).Replace('\','/'), [IO.Compression.CompressionLevel]::Optimal) | Out-Null } }
    finally { $zip.Dispose() }
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        if ($zip.Entries.Count -ne $evidence.Count) { throw 'Evidence archive count mismatch.' }
        foreach ($file in $evidence) {
            $entry = $zip.GetEntry([IO.Path]::GetRelativePath($jotRoot,$file.FullName).Replace('\','/'))
            $input = $entry.Open()
            try { $archivedHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($input)) } finally { $input.Dispose() }
            if ($archivedHash -ne (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash) { throw 'Evidence archive integrity check failed.' }
        }
    } finally { $zip.Dispose() }
    foreach ($target in $plan) {
        $absolute = Assert-JotChild $target.Path
        # Recheck descendants immediately before deletion; never follow links.
        @(Get-JotFiles $absolute) | Out-Null
        if (@(Get-JotRegisteredBuilds) | Where-Object { $_ -eq $absolute -or $_.StartsWith($absolute + '\', [StringComparison]::OrdinalIgnoreCase) }) { throw "Windows started referencing $absolute; cleanup stopped." }
        if (Get-CimInstance Win32_Process | Where-Object { ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($absolute + '\', [StringComparison]::OrdinalIgnoreCase)) -or ($_.Name -eq 'msedgewebview2.exe' -and $_.CommandLine -and $_.CommandLine.Contains($absolute, [StringComparison]::OrdinalIgnoreCase)) }) { throw "A process started using $absolute; cleanup stopped." }
        Write-Host ('Removing generated output: ' + [IO.Path]::GetRelativePath($jotRoot,$absolute))
        Remove-Item -LiteralPath $absolute -Recurse -Force
    }
    foreach ($path in $fingerprints.Keys) { if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $fingerprints[$path]) { throw "Protected file changed: $path" } }
    [pscustomobject]@{ RemovedBytes=[long](($plan | Measure-Object Bytes -Sum).Sum); EvidenceArchive=$archive; ArchivedEvidenceFiles=$evidence.Count; ArchiveBytes=(Get-Item -LiteralPath $archive).Length; ProtectedFilesVerified=$fingerprints.Count; KeptBuild=$keep; ProtectedBuilds=@($preservedBuilds | Sort-Object) } | ConvertTo-Json
} finally { $releaseLock.Dispose() }
