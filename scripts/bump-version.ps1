<#
    Increments the patch number of <Version> in SosLan.csproj and stages the change.
    Invoked automatically by the pre-commit hook (see .githooks/pre-commit).
#>

$ErrorActionPreference = "Stop"

$repoRoot = (& git rev-parse --show-toplevel).Trim()
$csprojPath = Join-Path $repoRoot "src\SosLan\SosLan.csproj"

$content = [System.IO.File]::ReadAllText($csprojPath, [System.Text.Encoding]::UTF8)

$match = [System.Text.RegularExpressions.Regex]::Match($content, '<Version>(\d+)\.(\d+)\.(\d+)</Version>')
if (-not $match.Success)
{
    Write-Output "bump-version: balise <Version> introuvable dans $csprojPath, aucune modification."
    exit 0
}

$major = [int]$match.Groups[1].Value
$minor = [int]$match.Groups[2].Value
$patch = [int]$match.Groups[3].Value + 1
$newVersion = "$major.$minor.$patch"

$newContent = $content.Substring(0, $match.Index) + "<Version>$newVersion</Version>" + $content.Substring($match.Index + $match.Length)

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($csprojPath, $newContent, $utf8NoBom)

& git add $csprojPath

Write-Output "bump-version: version -> $newVersion"
