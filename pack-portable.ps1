param(
    [ValidateSet('win-x64','win-x86','win-arm64')]
    [string]$Runtime = 'win-x64',
    [ValidateSet('Release','Debug')]
    [string]$Configuration = 'Release'
)

$projectPath = Join-Path $PSScriptRoot 'FileSizeTool.csproj'
$publishDir = Join-Path $PSScriptRoot "publish\$Configuration-$Runtime"
$zipOutput = Join-Path $PSScriptRoot "publish\FileSizeTool-$Configuration-$Runtime-portable.zip"

Write-Host "Publishing project to portable folder..."
Write-Host "Project: $projectPath"
Write-Host "Runtime: $Runtime"
Write-Host "Configuration: $Configuration"
Write-Host "Publish folder: $publishDir"
Write-Host "Package file: $zipOutput"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path $zipOutput) {
    Remove-Item $zipOutput -Force -ErrorAction SilentlyContinue
}

$publishArgs = @(
    'publish',
    $projectPath,
    '-c', $Configuration,
    '-r', $Runtime,
    '--self-contained', 'false',
    '-o', $publishDir,
    '/p:GenerateFullPaths=true',
    '/p:PublishSingleFile=false',
    '/p:UseAppHost=true'
)

$publishResult = dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (-not (Test-Path $publishDir)) {
    throw "Publish output folder was not created: $publishDir"
}

# Copy runtime-check script into publish dir to provide friendly prompt if .NET is missing
$runBat = Join-Path $PSScriptRoot 'run.bat'
if (Test-Path $runBat) {
    Copy-Item $runBat -Destination $publishDir -Force
} else {
    Write-Host "Warning: run.bat not found, publish package will not include runtime-check launcher."
}

Write-Host "Creating portable zip package..."
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipOutput -Force

Write-Host "Portable package created successfully: $zipOutput"
Write-Host ("Ready to distribute. Unzip and run FileSizeTool.exe in the publish directory: {0}\FileSizeTool.exe" -f $publishDir)