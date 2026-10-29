#requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$siteName = 'StansfeldScottPortalApi'
$poolName = 'StansfeldScottPortalApiPool'
$port = 5080
$publishPath = Join-Path $PSScriptRoot '_iis_publish'
$sitePath = Join-Path $env:SystemDrive 'inetpub\wwwroot\StansfeldScottPortalApi'
$appCmd = Join-Path $env:windir 'System32\inetsrv\appcmd.exe'

if (-not (Test-Path (Join-Path $publishPath 'StansfeldScott.Portal.Api.dll'))) {
    throw "Publish output is missing. Run dotnet publish first; expected: $publishPath"
}
if (-not (Test-Path (Join-Path $publishPath 'web.config'))) {
    throw "IIS web.config is missing from publish output: $publishPath"
}
if (-not (Test-Path $appCmd)) {
    throw 'IIS is not installed or appcmd.exe is missing.'
}

Import-Module WebAdministration
if (Get-Website -Name $siteName -ErrorAction SilentlyContinue) {
    throw "IIS site '$siteName' already exists. Stop here and review it in IIS Manager before rerunning."
}
if (Get-WebBinding | Where-Object { $_.bindingInformation -match ':5080:' }) {
    throw 'Port 5080 already has an IIS binding. Choose an unused port and update the frontend API URL and AllowedOrigins first.'
}
if (Get-NetTCPConnection -State Listen -LocalPort $port -ErrorAction SilentlyContinue) {
    throw 'Port 5080 is still occupied by a running process. Stop that process before creating the IIS site.'
}

New-Item -ItemType Directory -Path $sitePath -Force | Out-Null
Copy-Item -Path (Join-Path $publishPath '*') -Destination $sitePath -Recurse -Force

New-WebAppPool -Name $poolName
Set-ItemProperty "IIS:\AppPools\$poolName" -Name managedRuntimeVersion -Value ''
Set-ItemProperty "IIS:\AppPools\$poolName" -Name startMode -Value AlwaysRunning
Set-ItemProperty "IIS:\AppPools\$poolName" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty "IIS:\AppPools\$poolName" -Name processModel.loadUserProfile -Value $true

& icacls.exe $sitePath /grant "IIS AppPool\${poolName}:(OI)(CI)RX" /T /C | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'Could not grant the IIS app pool read access to the published API files.'
}

New-Website -Name $siteName -PhysicalPath $sitePath -Port $port -IPAddress '*' -ApplicationPool $poolName | Out-Null
Start-Website -Name $siteName

$ruleName = 'Stansfeld Scott Portal API (TCP 5080)'
if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Protocol TCP -LocalPort $port -Action Allow -Profile Domain,Private | Out-Null
}

Write-Host "API IIS site started: http://192.168.1.148:$port/swagger"
Write-Host "Site files: $sitePath"
