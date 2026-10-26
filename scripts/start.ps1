#requires -Version 7.0
[CmdletBinding()]
param(
    [ValidateRange(1, 65535)][int]$Port = 5000,
    [ValidateRange(1, 65535)][int]$WebPort = 5173,
    [string]$DatabasePath,
    [switch]$ApiOnly,
    [switch]$NoRestore,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$apiProcess = $null
$webProcess = $null
Push-Location $repositoryRoot
try {
    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    $sdk = & $dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^10\.') {
        throw 'Install the .NET 10 SDK specified in global.json (10.0.400 or a later patch in that feature band).'
    }
    if ([string]::IsNullOrWhiteSpace($DatabasePath)) {
        $DatabasePath = Join-Path $repositoryRoot 'data/atm.db'
    }
    if (-not [IO.Path]::IsPathFullyQualified($DatabasePath)) {
        throw 'DatabasePath must be an absolute file path.'
    }
    $node = $null
    $npm = $null
    if (-not $ApiOnly) {
        if ($Port -eq $WebPort) { throw 'Port and WebPort must be different.' }
        $node = (Get-Command node -ErrorAction Stop).Source
        $npm = (Get-Command npm -ErrorAction Stop).Source
        $nodeVersion = & $node --version
        if ($LASTEXITCODE -ne 0 -or $nodeVersion -notmatch '^v(\d+)\.(\d+)\.') { throw 'Could not determine the Node.js version.' }
        if ([int]$Matches[1] -lt 22 -or ([int]$Matches[1] -eq 22 -and [int]$Matches[2] -lt 12)) {
            throw 'Install Node.js 22.12 or newer for the frontend.'
        }
    }
    $ports = @($Port)
    if (-not $ApiOnly) { $ports += $WebPort }
    foreach ($listenPort in $ports) {
        $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $listenPort)
        $probe.Server.ExclusiveAddressUse = $true
        try { $probe.Start() }
        catch { throw "Port $listenPort is already occupied. Stop its owner or choose -Port / -WebPort with free ports." }
        finally { $probe.Stop() }
    }

    if (-not $NoBuild) {
        if (-not $NoRestore) {
            & $dotnet restore Atm.slnx --locked-mode
            if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
        }
        & $dotnet build src/Atm.Api/Atm.Api.csproj --configuration Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw 'API build failed.' }
        if (-not $ApiOnly) {
            Push-Location (Join-Path $repositoryRoot 'web')
            try {
                if (-not $NoRestore) {
                    & $npm ci --no-audit --no-fund
                    if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
                }
                & $npm run build
                if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
            }
            finally { Pop-Location }
        }
    }
    $apiDll = Join-Path $repositoryRoot 'src/Atm.Api/bin/Release/net10.0/Atm.Api.dll'
    if (-not (Test-Path -LiteralPath $apiDll)) { throw 'Release API build is missing. Run without -NoBuild.' }
    $webDirectory = Join-Path $repositoryRoot 'web'
    $vite = Join-Path $webDirectory 'node_modules/vite/bin/vite.js'
    if (-not $ApiOnly -and -not (Test-Path -LiteralPath $vite)) { throw 'Frontend dependencies are missing. Run without -NoBuild / -NoRestore.' }
    $logDirectory = Join-Path $repositoryRoot '.artifacts'
    New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
    $runId = [Guid]::NewGuid().ToString('N')
    $stdoutLog = Join-Path $logDirectory "api-$runId.out.log"
    $stderrLog = Join-Path $logDirectory "api-$runId.err.log"
    $baseUrl = "http://127.0.0.1:$Port"
    $startOptions = @{
        FilePath = $dotnet
        ArgumentList = @('"' + $apiDll + '"', '--urls', $baseUrl, '--contentRoot', '"' + (Split-Path $apiDll) + '"', '--Atm:DatabasePath', '"' + $DatabasePath + '"')
        WorkingDirectory = $repositoryRoot
        PassThru = $true
        RedirectStandardOutput = $stdoutLog
        RedirectStandardError = $stderrLog
    }
    if ($IsWindows) { $startOptions.WindowStyle = 'Hidden' }
    # The API applies migrations once on startup, preserving existing data.
    $apiProcess = Start-Process @startOptions
    $ready = $false
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while ([DateTime]::UtcNow -lt $deadline) {
        if ($apiProcess.HasExited) { throw "API exited during startup. See $stdoutLog and $stderrLog." }
        try {
            $accounts = Invoke-RestMethod "$baseUrl/api/accounts" -TimeoutSec 2
            if ($accounts.Count -eq 2) { $ready = $true; break }
        }
        catch { Start-Sleep -Milliseconds 200 }
    }
    if (-not $ready) { throw "API was not ready within 30 seconds. See $stdoutLog and $stderrLog." }
    Write-Host "API ready: $baseUrl/api/accounts"
    if (-not $ApiOnly) {
        $webStdout = Join-Path $logDirectory "web-$runId.out.log"
        $webStderr = Join-Path $logDirectory "web-$runId.err.log"
        $webUrl = "http://127.0.0.1:$WebPort"
        $webOptions = @{
            FilePath = $node
            ArgumentList = @('"' + $vite + '"', '--host', '127.0.0.1', '--port', $WebPort, '--strictPort')
            WorkingDirectory = $webDirectory
            PassThru = $true
            RedirectStandardOutput = $webStdout
            RedirectStandardError = $webStderr
        }
        if ($IsWindows) { $webOptions.WindowStyle = 'Hidden' }
        $previousTarget = $env:ATM_API_TARGET
        try {
            $env:ATM_API_TARGET = $baseUrl
            $webProcess = Start-Process @webOptions
        }
        finally { $env:ATM_API_TARGET = $previousTarget }
        $webReady = $false
        $deadline = [DateTime]::UtcNow.AddSeconds(30)
        while ([DateTime]::UtcNow -lt $deadline) {
            if ($apiProcess.HasExited -or $webProcess.HasExited) { throw "A service exited during UI startup. See $stdoutLog, $stderrLog, $webStdout and $webStderr." }
            try {
                $page = Invoke-WebRequest $webUrl -TimeoutSec 2
                $proxiedAccounts = Invoke-RestMethod "$webUrl/api/accounts" -TimeoutSec 2
                if ($page.StatusCode -eq 200 -and $proxiedAccounts.Count -eq 2) { $webReady = $true; break }
            }
            catch { Start-Sleep -Milliseconds 200 }
        }
        if (-not $webReady) { throw "UI or API proxy was not ready within 30 seconds. See $webStdout and $webStderr." }
        Write-Host "Open the app: $webUrl"
        Write-Host "UI logs: $webStdout and $webStderr"
    }
    Write-Host "Database: $DatabasePath"
    Write-Host "Logs: $stdoutLog and $stderrLog"
    Write-Host 'Press Ctrl+C to stop.'
    while (-not $apiProcess.HasExited -and ($null -eq $webProcess -or -not $webProcess.HasExited)) { Start-Sleep -Milliseconds 300 }
    if ($null -ne $webProcess -and $webProcess.HasExited) { throw "UI exited unexpectedly. See $webStdout and $webStderr." }
    if ($apiProcess.ExitCode -ne 0) { throw "API exited with code $($apiProcess.ExitCode). See $stderrLog." }
}
finally {
    if ($null -ne $webProcess) {
        if (-not $webProcess.HasExited) {
            $webProcess.Kill($true)
            $webProcess.WaitForExit()
        }
        $webProcess.Dispose()
    }
    if ($null -ne $apiProcess) {
        if (-not $apiProcess.HasExited) {
            $apiProcess.Kill($true)
            $apiProcess.WaitForExit()
        }
        $apiProcess.Dispose()
    }
    Pop-Location
}


