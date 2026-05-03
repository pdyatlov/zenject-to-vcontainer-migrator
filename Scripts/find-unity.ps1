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
        $hit = Get-ChildItem $root -Directory `
            | Where-Object { $_.Name -match "^$RequiredMajor\." } `
            | Sort-Object Name -Descending `
            | Select-Object -First 1
        if ($hit) { return Join-Path $hit.FullName "Editor\Unity.exe" }
    }
    throw "Unity $RequiredMajor.x not found. Set `$env:UNITY_PATH or install via Unity Hub."
}
