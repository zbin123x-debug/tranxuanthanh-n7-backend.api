$ErrorActionPreference = 'Stop'

$projectFile = Join-Path $PSScriptRoot '..\TranXuanThanh_2380614806_N7_API.csproj'
[xml]$project = Get-Content -LiteralPath $projectFile -Raw
$secretsId = [string]$project.Project.PropertyGroup.UserSecretsId
if ([string]::IsNullOrWhiteSpace($secretsId)) {
    throw 'UserSecretsId is missing from the API project.'
}

$secretsDirectory = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId"
$secretsFile = Join-Path $secretsDirectory 'secrets.json'
New-Item -ItemType Directory -Path $secretsDirectory -Force | Out-Null

$values = @{}
if (Test-Path -LiteralPath $secretsFile) {
    $existing = Get-Content -LiteralPath $secretsFile -Raw | ConvertFrom-Json
    foreach ($property in $existing.PSObject.Properties) {
        $values[$property.Name] = [string]$property.Value
    }
}

$securePassword = Read-Host 'Enter the 16-character Gmail App Password (input is hidden)' -AsSecureString
$pointer = [IntPtr]::Zero
$plainPassword = $null
try {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    $normalizedPassword = $plainPassword -replace '\s', ''
    if ($normalizedPassword.Length -ne 16) {
        throw 'A Gmail App Password must contain 16 characters. No setting was changed.'
    }

    $values['Smtp:Host'] = 'smtp.gmail.com'
    $values['Smtp:Port'] = '587'
    $values['Smtp:Username'] = 'zbin789x@gmail.com'
    $values['Smtp:From'] = 'zbin789x@gmail.com'
    $values['Smtp:Password'] = $normalizedPassword

    $json = $values | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText($secretsFile, $json, [Text.UTF8Encoding]::new($false))
    Write-Host 'Gmail sender settings saved in this Windows user profile.'
}
finally {
    if ($pointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
    $securePassword.Dispose()
    $plainPassword = $null
    $normalizedPassword = $null
    $values['Smtp:Password'] = $null
}
