[CmdletBinding()]
param(
    [string] $GatewayImage = 'codex-gateway:verify',
    [string] $RunnerImage = 'codex-gateway-runner:0.148.0',
    [switch] $SkipBuild
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$verificationId = [guid]::NewGuid().ToString('N')
$fixture = Join-Path $temporaryRoot "codex-gateway-container-$verificationId"
$gatewayContainer = "codex-gateway-smoke-$verificationId"
$runnerContainer = "codex-gateway-runner-smoke-$verificationId"
$discoveryContainer = "codex-gateway-discovery-smoke-$verificationId"
$dataVolume = "codex-gateway-data-smoke-$verificationId"
$authVolume = "codex-gateway-auth-smoke-$verificationId"
$locationPushed = $false

function Assert-NativeSuccess([string] $Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Read-AppServerResponse([System.Diagnostics.Process] $Process, [long] $Id) {
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $read = $Process.StandardOutput.ReadLineAsync()
        if (-not $read.Wait(10000)) {
            throw "Timed out waiting for Codex App Server response $Id."
        }

        $line = $read.Result
        if ($null -eq $line) {
            throw "Codex App Server stopped before response $Id."
        }

        $message = $line | ConvertFrom-Json
        if ($message.id -eq $Id) {
            return $message
        }
    }

    throw "Codex App Server did not return response $Id."
}

function Test-McpDiscoveryProtocol([string] $Image, [string] $ContainerName) {
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'docker'
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardInput = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $arguments = @(
        'run', '--rm', '--interactive', '--name', $ContainerName,
        '--read-only', '--user', '10001:10001',
        '--tmpfs', '/tmp:rw,nosuid,nodev,noexec,size=32m',
        '--tmpfs', '/codex-home:rw,nosuid,nodev,noexec,size=16m,mode=1777',
        '--env', 'CODEX_HOME=/codex-home',
        '--env', 'HOME=/codex-home',
        '--entrypoint', 'codex', $Image,
        'app-server', '--listen', 'stdio://', '--strict-config',
        '--disable', 'apps', '--disable', 'plugins',
        '--config', 'mcp_servers={}'
    )
    foreach ($argument in $arguments) {
        [void] $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $started = $false
    try {
        $started = $process.Start()
        Assert-True $started 'Codex App Server discovery smoke did not start.'
        $stderr = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.WriteLine('{"method":"initialize","id":1,"params":{"clientInfo":{"name":"gateway-smoke","title":"Gateway smoke","version":"1.0.0"}}}')
        $process.StandardInput.Flush()
        $initialize = Read-AppServerResponse $process 1
        Assert-True ($initialize.result.codexHome -eq '/codex-home') 'Discovery CODEX_HOME is not the writable ephemeral mount.'

        $process.StandardInput.WriteLine('{"method":"initialized","params":{}}')
        $process.StandardInput.WriteLine('{"method":"mcpServerStatus/list","id":2,"params":{"limit":100,"detail":"toolsAndAuthOnly"}}')
        $process.StandardInput.Flush()
        $catalog = Read-AppServerResponse $process 2
        Assert-True ($null -ne $catalog.result.data) 'MCP discovery response did not contain data.'
        $process.StandardInput.Close()
        Assert-True ($process.WaitForExit(5000)) 'Codex App Server discovery smoke did not stop after stdin closed.'
        Assert-True ($process.ExitCode -eq 0) "Codex App Server discovery smoke failed: $($stderr.Result)"
    }
    finally {
        if ($started -and -not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }

        $process.Dispose()
        & docker rm --force $ContainerName 2>$null | Out-Null
    }
}

try {
    Push-Location $repositoryRoot
    $locationPushed = $true

    if (-not $SkipBuild) {
        & docker build --target runner --tag $RunnerImage .
        Assert-NativeSuccess 'Runner image build'
        & docker build --target gateway --tag $GatewayImage .
        Assert-NativeSuccess 'Gateway image build'
    }

    & docker run --rm --entrypoint codex $RunnerImage --version
    Assert-NativeSuccess 'Pinned runner Codex CLI check'
    & docker run --rm --entrypoint bwrap $RunnerImage --version
    Assert-NativeSuccess 'Runner Bubblewrap check'
    & docker run --rm --entrypoint codex $GatewayImage --version
    Assert-NativeSuccess 'Gateway App Server CLI check'
    & docker run --rm --entrypoint docker $GatewayImage --version
    Assert-NativeSuccess 'Gateway Docker CLI check'

    Test-McpDiscoveryProtocol $RunnerImage $discoveryContainer

    $runnerImageDetails = & docker image inspect $RunnerImage | ConvertFrom-Json
    Assert-NativeSuccess 'Runner image inspect'
    Assert-True ($runnerImageDetails[0].Config.User -eq '10001:10001') 'Runner image is not configured for UID/GID 10001.'
    Assert-True ($runnerImageDetails[0].Config.Entrypoint[0] -eq 'codex') 'Runner image does not default to the Codex entrypoint.'

    $workspaceDirectory = Join-Path $fixture 'workspace'
    $artifactDirectory = Join-Path $workspaceDirectory 'artifacts'
    $codexHomeDirectory = Join-Path $fixture 'codex-home'
    New-Item -ItemType Directory -Path $artifactDirectory, $codexHomeDirectory -Force | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $codexHomeDirectory 'auth.json'),
        'auth-secret',
        [System.Text.UTF8Encoding]::new($false))

    $smokeScript = @'
set -eu
printf 'artifact-ok\n' > artifacts/smoke.txt
printf 'tmp-ok\n' > /tmp/codex-gateway-smoke.txt
test "$(cat /tmp/codex-gateway-smoke.txt)" = 'tmp-ok'
if cat /codex-home/auth.json >/dev/null 2>&1; then
  echo 'CODEX_HOME was readable by a model command' >&2
  exit 21
fi
if cat /proc/1/root/codex-home/auth.json >/dev/null 2>&1; then
  echo 'the outer CODEX_HOME was reachable through /proc/1/root' >&2
  exit 22
fi
if printf 'forbidden\n' > /etc/codex-gateway-forbidden 2>/dev/null; then
  echo 'read-only container root was writable' >&2
  exit 23
fi
cat artifacts/smoke.txt
'@
    [System.IO.File]::WriteAllText(
        (Join-Path $workspaceDirectory 'smoke.sh'),
        $smokeScript.Replace("`r`n", "`n"),
        [System.Text.UTF8Encoding]::new($false))

    $fileSystemProfile = 'permissions.gateway_run.filesystem={":minimal"="read",":workspace_roots"="write",":tmpdir"="write",":slash_tmp"="write"}'
    $commonRunnerArguments = @(
        'run', '--rm', '--init',
        '--read-only',
        '--cap-drop', 'ALL',
        '--security-opt', 'no-new-privileges',
        '--security-opt', 'seccomp=unconfined',
        '--memory', '2048m',
        '--cpus', '2',
        '--pids-limit', '256',
        '--tmpfs', '/tmp:rw,nosuid,nodev,noexec,size=256m',
        '--network', 'bridge',
        '--mount', "type=bind,source=$workspaceDirectory,target=/workspace",
        '--mount', "type=bind,source=$codexHomeDirectory,target=/codex-home",
        '--entrypoint', 'codex',
        $RunnerImage,
        'sandbox',
        '-P', 'gateway_run',
        '-c', $fileSystemProfile,
        '-c', 'permissions.gateway_run.network.enabled=false',
        '-C', '/workspace',
        '--'
    )

    & docker @commonRunnerArguments /bin/sh smoke.sh
    Assert-NativeSuccess 'Runner filesystem sandbox check'

    & docker @commonRunnerArguments node -e "require('net').connect({host:'1.1.1.1',port:80}).on('connect',()=>process.exit(23)).on('error',error=>process.exit(error.code==='EPERM'?0:24)).setTimeout(3000,()=>process.exit(25))"
    Assert-NativeSuccess 'Runner command-network sandbox check'

    # Keep one container alive long enough to inspect the outer isolation contract.
    & docker run -d `
        --name $runnerContainer `
        --label com.codex-gateway.managed=true `
        --label "com.codex-gateway.instance-id=$verificationId" `
        --label "com.codex-gateway.run-id=$verificationId" `
        --init `
        --read-only `
        --cap-drop ALL `
        --security-opt no-new-privileges `
        --security-opt seccomp=unconfined `
        --memory 2048m `
        --memory-swap 2048m `
        --cpus 2 `
        --pids-limit 256 `
        --tmpfs /tmp:rw,nosuid,nodev,noexec,size=256m `
        --network bridge `
        --mount "type=bind,source=$workspaceDirectory,target=/workspace" `
        --mount "type=bind,source=$codexHomeDirectory,target=/codex-home" `
        --entrypoint codex `
        $RunnerImage `
        sandbox `
        -P gateway_run `
        -c $fileSystemProfile `
        -c 'permissions.gateway_run.network.enabled=false' `
        -C /workspace `
        -- /bin/sh -c 'sleep 60' | Out-Null
    Assert-NativeSuccess 'Inspectable runner start'

    $runnerDetails = & docker inspect $runnerContainer | ConvertFrom-Json
    Assert-NativeSuccess 'Runner container inspect'
    $runnerHost = $runnerDetails[0].HostConfig
    Assert-True $runnerHost.ReadonlyRootfs 'Runner root filesystem is not read-only.'
    Assert-True ($runnerHost.CapDrop -contains 'ALL') 'Runner capabilities were not dropped.'
    Assert-True (($runnerHost.SecurityOpt -join ',') -match 'no-new-privileges') 'Runner does not set no-new-privileges.'
    Assert-True (($runnerHost.SecurityOpt -join ',') -match 'seccomp=unconfined') 'Runner cannot start Bubblewrap under its outer seccomp profile.'
    Assert-True ($runnerHost.Memory -eq 2147483648) 'Runner memory limit is not 2048 MiB.'
    Assert-True ($runnerHost.MemorySwap -eq 2147483648) 'Runner swap limit does not match its memory limit.'
    Assert-True ($runnerHost.NanoCpus -eq 2000000000) 'Runner CPU limit is not 2 CPUs.'
    Assert-True ($runnerHost.PidsLimit -eq 256) 'Runner PID limit is not 256.'
    Assert-True ($runnerHost.NetworkMode -eq 'bridge') 'Runner network is not the configured bridge.'
    Assert-True $runnerHost.Init 'Runner does not use Docker init for descendant reaping.'
    Assert-True ($runnerDetails[0].Config.Entrypoint[0] -eq 'codex') 'Runner does not force the Codex entrypoint.'
    Assert-True ($runnerDetails[0].Config.Labels.'com.codex-gateway.managed' -eq 'true') 'Managed-container label is missing.'
    Assert-True ($runnerDetails[0].Config.Labels.'com.codex-gateway.instance-id' -eq $verificationId) 'Gateway instance label is missing.'
    Assert-True ($runnerDetails[0].Config.Labels.'com.codex-gateway.run-id' -eq $verificationId) 'Run ID label is missing.'
    Assert-True (($runnerDetails[0].Mounts.Destination) -contains '/workspace') 'Workspace mount is missing.'
    Assert-True (($runnerDetails[0].Mounts.Destination) -contains '/codex-home') 'CODEX_HOME mount is missing.'

    & docker rm --force $runnerContainer | Out-Null
    Assert-NativeSuccess 'Inspectable runner cleanup'

    & docker volume create $dataVolume | Out-Null
    Assert-NativeSuccess 'Gateway data volume create'
    & docker volume create $authVolume | Out-Null
    Assert-NativeSuccess 'Gateway auth volume create'

    $startedGateway = & docker run -d `
        --name $gatewayContainer `
        --group-add 0 `
        --security-opt no-new-privileges `
        -p '127.0.0.1::8080' `
        -e AdminUi__Username=container-smoke-admin `
        -e AdminUi__Password=container-smoke-password `
        -e Codex__Container__Image=$RunnerImage `
        -e Codex__Container__WorkspaceVolume=$dataVolume `
        -e Codex__Container__AuthVolume=$authVolume `
        -v "${dataVolume}:/app/data" `
        -v "${authVolume}:/app/.codex-home" `
        -v /var/run/docker.sock:/var/run/docker.sock `
        $GatewayImage
    Assert-NativeSuccess 'Gateway container start'
    Assert-True (-not [string]::IsNullOrWhiteSpace($startedGateway)) 'Docker did not return a gateway container ID.'

    $mapping = & docker port $gatewayContainer 8080/tcp
    Assert-NativeSuccess 'Gateway port lookup'
    $port = ($mapping.Trim() -split ':')[-1]
    $baseUrl = "http://127.0.0.1:$port"
    $healthy = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        try {
            $health = Invoke-WebRequest -Uri "$baseUrl/health" -UseBasicParsing -TimeoutSec 2
            if ($health.StatusCode -eq 200) {
                $healthy = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 500
        }
    }

    if (-not $healthy) {
        & docker logs $gatewayContainer
        throw 'Gateway container did not become healthy.'
    }

    & docker exec $gatewayContainer docker version --format '{{.Client.Version}}' | Out-Null
    Assert-NativeSuccess 'Gateway-to-Docker-engine check'

    $admin = Invoke-WebRequest -Uri "$baseUrl/admin" -UseBasicParsing -TimeoutSec 5
    Assert-True ($admin.StatusCode -eq 200) "Admin UI returned HTTP $($admin.StatusCode)."

    $blazor = Invoke-WebRequest -Uri "$baseUrl/_framework/blazor.web.js" -UseBasicParsing -TimeoutSec 5
    Assert-True ($blazor.StatusCode -eq 200) "Blazor framework returned HTTP $($blazor.StatusCode)."
    Assert-True ($blazor.RawContentLength -gt 1000) 'Blazor framework response was unexpectedly empty.'

    try {
        Invoke-WebRequest -Uri "$baseUrl/v1/models" -UseBasicParsing -TimeoutSec 5 | Out-Null
        throw 'Unauthenticated OpenAI API request unexpectedly succeeded.'
    }
    catch {
        if ($_.Exception.Response.StatusCode.value__ -ne 401) {
            throw
        }
    }

    & docker rm --force $gatewayContainer | Out-Null
    Assert-NativeSuccess 'Gateway container cleanup'

    $stale = & docker ps --all --quiet --filter "label=com.codex-gateway.run-id=$verificationId"
    Assert-NativeSuccess 'Stale runner lookup'
    Assert-True ([string]::IsNullOrWhiteSpace(($stale -join ''))) 'A labelled verification runner was left behind.'

    Write-Output 'Runner image, hardened container contract, inner filesystem/network sandbox, gateway health/UI/auth, Docker access, and cleanup checks passed.'
}
finally {
    & docker rm --force $runnerContainer $gatewayContainer $discoveryContainer 2>$null | Out-Null
    & docker volume rm --force $dataVolume $authVolume 2>$null | Out-Null

    if ($locationPushed) {
        Pop-Location -ErrorAction SilentlyContinue
    }

    $resolvedFixture = [System.IO.Path]::GetFullPath($fixture)
    if ($resolvedFixture.StartsWith($temporaryRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedFixture).StartsWith('codex-gateway-container-', [System.StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
