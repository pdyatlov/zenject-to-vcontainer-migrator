# Resolves a Unity 2021.3+ install path. Honours $env:UNITY_PATH; otherwise
# probes Unity Hub install dirs. Writes to $script:UnityPath.

function Resolve-Unity {
    param([string]$RequiredMajor = "2021")
    if ($env:UNITY_PATH -and (Test-Path $env:UNITY_PATH)) { return $env:UNITY_PATH }
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor",
        "$env:LOCALAPPDATA\Unity\Hub\Editor"
    )
    foreach ($root in $candidates) {
        if (-not (Test-Path $root)) { continue }
        # Sort by parsed [Version] so 2021.3.10f1 ranks above 2021.3.9f1
        # (alphabetic Sort would put 9f1 above 10f1). Strip the trailing
        # release-letter suffix (f1/b1/a1/etc.) before parsing.
        $hit = Get-ChildItem $root -Directory `
            | Where-Object { $_.Name -match "^$RequiredMajor\." } `
            | Sort-Object { [Version]($_.Name -replace '[a-zA-Z]\d+$','') } -Descending `
            | Select-Object -First 1
        if ($hit) { return Join-Path $hit.FullName "Editor\Unity.exe" }
    }
    throw "Unity $RequiredMajor.x not found. Set `$env:UNITY_PATH or install via Unity Hub."
}
