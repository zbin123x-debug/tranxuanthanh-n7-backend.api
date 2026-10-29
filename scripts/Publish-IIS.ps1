param(
    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$project = Join-Path $PSScriptRoot '..\TranXuanThanh_2380614806_N7_API.csproj'
$apiRoot = Split-Path -Parent $PSScriptRoot
$deployment = Join-Path $apiRoot 'artifacts\deployment'
$tempPublish = Join-Path $env:TEMP ('n7-api-iis-' + [guid]::NewGuid().ToString('N'))
$zipPath = Join-Path $deployment 'api-iis-win-x64.zip'

New-Item -ItemType Directory -Force -Path $deployment | Out-Null
dotnet publish $project --configuration Release --runtime $Runtime --self-contained false --output $tempPublish
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Production uses hosting environment variables, never the Development profile.
$developmentSettings = Join-Path $tempPublish 'appsettings.Development.json'
if (Test-Path -LiteralPath $developmentSettings) {
    Remove-Item -LiteralPath $developmentSettings -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $tempPublish '*') -DestinationPath $zipPath -CompressionLevel Optimal

$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $hasNestedArtifacts = $archive.Entries | Where-Object { $_.FullName -match '(^|/)artifacts/' }
    if ($hasNestedArtifacts) {
        throw 'Publish archive contains the repository artifacts folder; refusing to keep the contaminated package.'
    }
    if (-not ($archive.Entries | Where-Object FullName -eq 'web.config')) {
        throw 'Publish archive is missing web.config.'
    }
    if ($archive.Entries | Where-Object FullName -eq 'appsettings.Development.json') {
        throw 'Production publish archive must not contain appsettings.Development.json.'
    }
}
finally {
    $archive.Dispose()
}

Get-Item -LiteralPath $zipPath | Select-Object FullName, Length, LastWriteTime
