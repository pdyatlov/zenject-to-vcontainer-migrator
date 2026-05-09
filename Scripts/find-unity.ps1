# Resolves a Unity install path. Resolution order:
#   1. $env:UNITY_PATH if it points at a real file.
#   2. Exact match against the host's pinned editor when -ProjectPath is given
#      (read from ProjectSettings/ProjectVersion.txt).
#   3. Newest installed editor satisfying -MinVersion (default 2021.3 — the
#      package's minimum).
# Hub install roots probed: C:\Program Files\Unity\Hub\Editor and
# %LOCALAPPDATA%\Unity\Hub\Editor.

function Resolve-Unity {
    param(
        [string]$MinVersion = "2021.3",
        [string]$ProjectPath
    )
    if ($env:UNITY_PATH -and (Test-Path $env:UNITY_PATH)) { return $env:UNITY_PATH }

    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor",
        "$env:LOCALAPPDATA\Unity\Hub\Editor"
    )

    if ($ProjectPath) {
        $versionFile = Join-Path $ProjectPath "ProjectSettings\ProjectVersion.txt"
        if (Test-Path $versionFile) {
            $line = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(\S+)' `
                | Select-Object -First 1
            if ($line) {
                $pinned = $line.Matches[0].Groups[1].Value
                foreach ($root in $candidates) {
                    $exe = Join-Path $root "$pinned\Editor\Unity.exe"
                    if (Test-Path $exe) { return $exe }
                }
                Write-Warning "Pinned editor $pinned not installed; falling back to newest >= $MinVersion."
            }
        }
    }

    $min = [Version]$MinVersion
    foreach ($root in $candidates) {
        if (-not (Test-Path $root)) { continue }
        # Strip trailing release-letter suffix (f1/b1/a1/etc.) before parsing
        # as [Version]. Sort by parsed version so 2021.3.10f1 ranks above
        # 2021.3.9f1 (alphabetic Sort would put 9f1 above 10f1).
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
