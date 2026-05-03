# Resolves a Unity install path satisfying a minimum version. The package
# requires Unity 2021.3+, so the default minimum is 2021.3. Honours
# $env:UNITY_PATH; otherwise probes Unity Hub install dirs and picks the
# newest install that meets the minimum.

function Resolve-Unity {
    param([string]$MinVersion = "2021.3")
    if ($env:UNITY_PATH -and (Test-Path $env:UNITY_PATH)) { return $env:UNITY_PATH }
    $min = [Version]$MinVersion
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor",
        "$env:LOCALAPPDATA\Unity\Hub\Editor"
    )
    foreach ($root in $candidates) {
        if (-not (Test-Path $root)) { continue }
        # Strip the trailing release-letter suffix (f1/b1/a1/etc.) before
        # parsing as [Version], then keep only installs >= MinVersion. Sort
        # by parsed version so 2021.3.10f1 ranks above 2021.3.9f1
        # (alphabetic Sort would put 9f1 above 10f1).
        $hit = Get-ChildItem $root -Directory `
            | ForEach-Object {
                $stripped = $_.Name -replace '[a-zA-Z]\d+$',''
                $parsed = $null
                if ([Version]::TryParse($stripped, [ref]$parsed)) {
                    [pscustomobject]@{ Dir = $_; Version = $parsed }
                }
            } `
            | Where-Object { $_.Version -ge $min } `
            | Sort-Object Version -Descending `
            | Select-Object -First 1
        if ($hit) { return Join-Path $hit.Dir.FullName "Editor\Unity.exe" }
    }
    throw "Unity $MinVersion or newer not found. Set `$env:UNITY_PATH or install via Unity Hub."
}
