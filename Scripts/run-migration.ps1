param(
    [Parameter(Mandatory)] [string]$ProjectPath,
    [string]$OutputDir = "$ProjectPath\Library\Zenject2VContainer\headless",
    [string]$LogPath = "$ProjectPath\Logs\unity-migration.log"
)
. "$PSScriptRoot\find-unity.ps1"
$unity = Resolve-Unity
New-Item -ItemType Directory -Force -Path (Split-Path $LogPath) | Out-Null
& $unity -batchmode -nographics -projectPath $ProjectPath `
    -executeMethod Zenject2VContainer.Headless.MigrationCli.RunFullEntry `
    -projectRoot $ProjectPath -outputDir $OutputDir -quit -logFile $LogPath
exit $LASTEXITCODE
