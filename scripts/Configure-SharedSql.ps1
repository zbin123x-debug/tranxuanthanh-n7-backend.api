$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot '..\TranXuanThanh_2380614806_N7_API.csproj'
$project = [IO.Path]::GetFullPath($project)

# Ask for SQL credentials without displaying the password or placing it in command history.
$sqlUser = Read-Host 'SQL Server username'
$securePassword = Read-Host 'SQL Server password (hidden)' -AsSecureString
$pointer = [IntPtr]::Zero
$plainPassword = $null
try {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)

    $builder = [System.Data.Common.DbConnectionStringBuilder]::new()
    $builder['Server'] = 'sql9001.site4now.net'
    $builder['Database'] = 'db_ad06d2_tranxuanthanhn7'
    $builder['User ID'] = $sqlUser
    $builder['Password'] = $plainPassword
    $builder['Encrypt'] = 'True'
    $builder['TrustServerCertificate'] = 'True'

    $secretsId = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.UserSecretsId | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($secretsId)) {
        $null = dotnet user-secrets init --project $project 2>&1
        if ($LASTEXITCODE -ne 0) { throw 'Could not initialize .NET User Secrets.' }
        $secretsId = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.UserSecretsId | Select-Object -First 1
    }
    if ([string]::IsNullOrWhiteSpace($secretsId)) { throw 'UserSecretsId is missing from the project.' }

    $folder = Join-Path $env:APPDATA "Microsoft\UserSecrets\$secretsId"
    $secretFile = Join-Path $folder 'secrets.json'
    New-Item -ItemType Directory -Force -Path $folder | Out-Null
    $settings = [ordered]@{}
    if (Test-Path -LiteralPath $secretFile) {
        $old = Get-Content -LiteralPath $secretFile -Raw | ConvertFrom-Json
        foreach ($property in $old.PSObject.Properties) { $settings[$property.Name] = $property.Value }
    }
    $connections = [ordered]@{}
    if ($settings.Contains('ConnectionStrings') -and $null -ne $settings['ConnectionStrings']) {
        $oldConnections = $settings['ConnectionStrings']
        if ($oldConnections -is [System.Collections.IDictionary]) {
            foreach ($key in $oldConnections.Keys) { $connections[$key] = $oldConnections[$key] }
        } else {
            foreach ($property in $oldConnections.PSObject.Properties) { $connections[$property.Name] = $property.Value }
        }
    }
    $connections['DefaultConnection'] = $builder.ConnectionString
    $settings['ConnectionStrings'] = $connections
    $json = $settings | ConvertTo-Json -Depth 20
    Set-Content -LiteralPath $secretFile -Value $json -Encoding utf8
    Write-Host 'Saved the shared SQL connection in .NET User Secrets (outside the source tree).'
    Write-Host 'No migrations were run and no SQL data was changed.'
}
finally {
    if ($pointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
    if ($securePassword) { $securePassword.Dispose() }
    $plainPassword = $null
    $sqlUser = $null
}
