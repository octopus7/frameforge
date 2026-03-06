param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectDir
)

$ErrorActionPreference = "Stop"

$resolvedProjectDir = (Resolve-Path $ProjectDir).Path
$allowedThemeFile = [System.IO.Path]::Combine($resolvedProjectDir, "Themes", "DarkTheme.xaml")
$colorPattern = '#[0-9A-Fa-f]{6}([0-9A-Fa-f]{2})?'

$xamlFiles = Get-ChildItem -Path $resolvedProjectDir -Recurse -File -Filter *.xaml |
    Where-Object {
        $_.FullName -notlike "*\\bin\\*" -and
        $_.FullName -notlike "*\\obj\\*" -and
        $_.FullName -ne $allowedThemeFile
    }

$violations = [System.Collections.Generic.List[object]]::new()

foreach ($xamlFile in $xamlFiles) {
    $lines = Get-Content -Path $xamlFile.FullName -Encoding UTF8

    for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
        if ($lines[$lineIndex] -match $colorPattern) {
            $violations.Add([PSCustomObject]@{
                    File = $xamlFile.FullName
                    Line = $lineIndex + 1
                    Text = $lines[$lineIndex].Trim()
                })
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "Theme validation failed: inline hex colors are not allowed outside Themes\\DarkTheme.xaml."
    foreach ($violation in $violations) {
        Write-Host "$($violation.File):$($violation.Line) $($violation.Text)"
    }

    exit 1
}

Write-Host "Theme validation passed."
