$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$dotnet = Join-Path $env:USERPROFILE ".dotnet\dotnet.exe"
$distRoot = Join-Path $projectRoot "dist"
$publishDir = Join-Path $distRoot "MemoryMonitorBall"
$projectFile = Join-Path $projectRoot "MemoryMonitorBall.csproj"

if (-not (Test-Path $dotnet)) {
    throw "dotnet.exe was not found at $dotnet. Install .NET 8 SDK first."
}

if (Test-Path $publishDir) {
    try {
        Remove-Item -LiteralPath $publishDir -Recurse -Force
    }
    catch {
        $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
        $publishDir = Join-Path $distRoot "MemoryMonitorBall-$timestamp"
        Write-Warning "Existing publish directory is locked. Publishing to $publishDir instead."
    }
}

& $dotnet publish $projectFile `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishReadyToRun=false `
    -o $publishDir

$exePath = Join-Path $publishDir "MemoryMonitorBall.exe"
if (-not (Test-Path $exePath)) {
    throw "Published executable was not found at $exePath."
}

$desktop = [Environment]::GetFolderPath("Desktop")
$shortcutName = "$([char]0x5185)$([char]0x5B58)$([char]0x76D1)$([char]0x63A7)$([char]0x7403).lnk"
$shortcutPath = Join-Path $desktop $shortcutName

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $publishDir
$shortcut.IconLocation = "$exePath,0"
$shortcut.Description = "$([char]0x5185)$([char]0x5B58)$([char]0x76D1)$([char]0x63A7)$([char]0x7403)"
$shortcut.Save()

# Enable "Run as administrator" on the shortcut so per-process TCP byte
# statistics can be enabled by Windows.
$shortcutBytes = [System.IO.File]::ReadAllBytes($shortcutPath)
$shortcutBytes[0x15] = $shortcutBytes[0x15] -bor 0x20
[System.IO.File]::WriteAllBytes($shortcutPath, $shortcutBytes)

Write-Host "Published to: $publishDir"
Write-Host "Shortcut created: $shortcutPath"
