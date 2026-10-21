$ErrorActionPreference = "Stop"

$PackageId = "Bitbound.GitNotesMcp"
$RepoDir = $PSScriptRoot
$VersionPrefix = "1.0.0"

$LocalSourceDir = if ([string]::IsNullOrWhiteSpace($env:NUGET_LOCAL_SOURCE)) {
    [IO.Path]::Combine($HOME, ".nuget", "local-packages")
} else {
    $env:NUGET_LOCAL_SOURCE
}

if (-not (Test-Path -Path $LocalSourceDir)) {
    New-Item -ItemType Directory -Path $LocalSourceDir -Force | Out-Null
}

$VersionPattern = "^" + [regex]::Escape($PackageId) + '\.(\d+\.\d+\.\d+)$'
$highest = $null

foreach ($package in Get-ChildItem -Path $LocalSourceDir -Filter "$PackageId.*.nupkg" -File) {
    if ($package.BaseName -notmatch $VersionPattern) {
        continue
    }

    $candidate = [Version]$Matches[1]
    if ($null -eq $highest -or $candidate -gt $highest) {
        $highest = $candidate
    }
}

if ($null -ne $highest) {
    $VersionPrefix = "{0}.{1}.{2}" -f $highest.Major, $highest.Minor, ($highest.Build + 1)
}

$ProjectFile = Join-Path (Join-Path $RepoDir "Bitbound.GitNotesMcp") "Bitbound.GitNotesMcp.csproj"

dotnet pack $ProjectFile -c Release -p:VersionPrefix=$VersionPrefix -o $LocalSourceDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Published $PackageId $VersionPrefix"
Write-Host "Source   : $LocalSourceDir"
Write-Host "Package  : $(Join-Path $LocalSourceDir "$PackageId.$VersionPrefix.nupkg")"
