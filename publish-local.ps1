$VersionPrefix = "1.0.0"
$LocalSourceDir = $env:NUGET_LOCAL_SOURCE
$RepoDir = $PSScriptRoot

if (-Not (Test-Path -Path $RepoDir)) {
    throw "Repository directory not found: $RepoDir"
}

$PackageId = "Bitbound.GitNotesMcp"
$VersionPattern = "^" + [regex]::Escape($PackageId) + '\.(\d+\.\d+\.\d+)$'

$highest = $null
if (Test-Path -Path $LocalSourceDir) {
    foreach ($package in Get-ChildItem -Path $LocalSourceDir -Filter "$PackageId.*.nupkg" -File) {
        if ($package.BaseName -notmatch $VersionPattern) {
            continue
        }

        $candidate = [Version]$Matches[1]
        if ($null -eq $highest -or $candidate -gt $highest) {
            $highest = $candidate
        }
    }
}

if ($null -ne $highest) {
    $VersionPrefix = "{0}.{1}.{2}" -f $highest.Major, $highest.Minor, ($highest.Build + 1)
    Write-Host "Found $PackageId $highest in $LocalSourceDir, publishing $($VersionPrefix)"
}

$ProjectDir = Join-Path $RepoDir "Bitbound.GitNotesMcp"
dotnet pack -c Release -p:VersionPrefix=$VersionPrefix -o $LocalSourceDir $ProjectDir
$PackExitCode = $LASTEXITCODE

$Artifact = Get-Item -Path (Join-Path $LocalSourceDir "$PackageId.$VersionPrefix.nupkg") -ErrorAction SilentlyContinue
$PreviousVersion = if ($null -eq $highest) { "first release" } else { "was $highest" }
$Published = if ($null -eq $Artifact) { "not produced" } else { $Artifact.FullName }

Write-Host ""
Write-Host "Publish summary"
Write-Host "---------------"
Write-Host "Package   : $PackageId"
Write-Host "Version   : $VersionPrefix ($PreviousVersion)"
Write-Host "Source    : $LocalSourceDir"
Write-Host "Artifact  : $Published"
Write-Host "Exit code : $PackExitCode"

if ($PackExitCode -ne 0) {
    throw "dotnet pack failed with exit code $PackExitCode."
} 