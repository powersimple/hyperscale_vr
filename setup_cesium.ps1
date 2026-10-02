# Adds the Cesium package registry and Cesium for Unity to Packages/manifest.json.
# Run once from the project folder, before opening the project in Unity.
$ErrorActionPreference = "Stop"
$manifestPath = Join-Path $PSScriptRoot "Packages\manifest.json"
if (-not (Test-Path $manifestPath)) { throw "No Packages\manifest.json here; run this in the Unity project folder." }
$m = Get-Content $manifestPath -Raw | ConvertFrom-Json
if (-not $m.PSObject.Properties["scopedRegistries"]) { $m | Add-Member -NotePropertyName scopedRegistries -NotePropertyValue @() }
if (-not ($m.scopedRegistries | Where-Object { $_.url -eq "https://unity.pkg.cesium.com" })) {
  $m.scopedRegistries = @($m.scopedRegistries) + [pscustomobject]@{ name = "Cesium"; url = "https://unity.pkg.cesium.com"; scopes = @("com.cesium.unity") }
}
if (-not $m.dependencies.PSObject.Properties["com.cesium.unity"]) {
  $m.dependencies | Add-Member -NotePropertyName "com.cesium.unity" -NotePropertyValue "1.26.0"
}
# Written without a byte-order mark, which the Package Manager does not accept.
[IO.File]::WriteAllText($manifestPath, ($m | ConvertTo-Json -Depth 20))
Write-Host "Cesium for Unity added. Open the project in Unity."
