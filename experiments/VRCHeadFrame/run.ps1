param(
    [string]$Unity = 'C:\Program Files\Unity 2022.3.22f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Unity)) { throw "Unity executable not found: $Unity" }
$version = (& $Unity -version | Out-String).Trim()
if ($version -ne '2022.3.22f1') { throw "Expected Unity 2022.3.22f1; got $version" }
# Copy source to a NEW case-insensitive staging folder; never open VLatProject here.
$stage = Join-Path ([IO.Path]::GetTempPath()) ('VLatHeadFrame-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage | Out-Null
foreach ($folder in @('Assets', 'Packages', 'ProjectSettings')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $folder) -Destination $stage -Recurse
}
$results = Join-Path $stage 'results'
$log = Join-Path $stage 'sweep.log'
# Deliberately omit -quit: EditorApplication.update advances the experiment coroutine.
# Deliberately omit -nographics: Shader readback MUST run on a graphics device.
& $Unity -batchmode -force-d3d11 -projectPath $stage `
    -executeMethod VLat.HeadFrameExperiment.HeadFrameSweep.RunBatch `
    -headFrameOutput $results -logFile $log
if ($LASTEXITCODE -ne 0) { throw "Unity experiment failed; see $log" }
Write-Output "Results: $results"
Write-Output "Log: $log"
Write-Output 'Check angles.csv and matrices.csv with scripts/check_head_frame_results.py.'
