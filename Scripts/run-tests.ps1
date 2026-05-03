param(
    [Parameter(Mandatory)] [string]$ProjectPath,
    [string]$ResultsPath = "$ProjectPath\Logs\test-results.xml",
    [string]$LogPath = "$ProjectPath\Logs\unity-test.log",
    [string]$TestPlatform = "EditMode"
)
. "$PSScriptRoot\find-unity.ps1"
$unity = Resolve-Unity
New-Item -ItemType Directory -Force -Path (Split-Path $ResultsPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $LogPath) | Out-Null
& $unity -batchmode -nographics -projectPath $ProjectPath `
    -runTests -testPlatform $TestPlatform `
    -testResults $ResultsPath -logFile $LogPath
exit $LASTEXITCODE
