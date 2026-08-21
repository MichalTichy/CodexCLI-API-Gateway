[CmdletBinding()]
param(
	[string] $Model,
	[string] $CodexHomePath,
	[int] $StartupTimeoutSeconds = 90,
	[int] $RequestTimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dataPath = Join-Path ([System.IO.Path]::GetTempPath()) ("codex-gateway-aspire-live-" + [guid]::NewGuid().ToString('N'))
$eventsPath = Join-Path $dataPath 'docker-events.log'
$gatewayProcess = $null
$eventsProcess = $null
$succeeded = $false

function Assert-NativeSuccess([string] $Step) {
	if ($LASTEXITCODE -ne 0) {
		throw "$Step failed with exit code $LASTEXITCODE."
	}
}

function Wait-ForHealth([string] $BaseUrl, [int] $TimeoutSeconds) {
	$deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
	while ([DateTime]::UtcNow -lt $deadline) {
		try {
			$response = Invoke-WebRequest -Uri "$BaseUrl/health" -UseBasicParsing -TimeoutSec 2
			if ($response.StatusCode -eq 200) {
				return
			}
		}
		catch {
			Start-Sleep -Milliseconds 500
		}
	}

	throw "The Aspire gateway did not become healthy within $TimeoutSeconds seconds."
}

try {
	if ([string]::IsNullOrWhiteSpace($CodexHomePath)) {
		$aspireCodexHome = Join-Path $PSScriptRoot '..\.aspire\codex-home'
		$localCodexHome = Join-Path $HOME '.codex'
		$CodexHomePath = if (Test-Path -LiteralPath (Join-Path $aspireCodexHome 'auth.json') -PathType Leaf) {
			$aspireCodexHome
		}
		elseif (Test-Path -LiteralPath (Join-Path $localCodexHome 'auth.json') -PathType Leaf) {
			$localCodexHome
		}
		else {
			$aspireCodexHome
		}
	}

	if (-not (Test-Path -LiteralPath $CodexHomePath -PathType Container)) {
		throw "Codex home '$CodexHomePath' does not exist. Authenticate Codex first or pass -CodexHomePath."
	}

	if (-not (Test-Path -LiteralPath (Join-Path $CodexHomePath 'auth.json') -PathType Leaf)) {
		throw "Codex home '$CodexHomePath' does not contain auth.json. Complete Codex device login before running this paid live test."
	}

	& docker image inspect codex-gateway-runner:0.148.0 | Out-Null
	Assert-NativeSuccess 'Runner image lookup'

	New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
	@{
		version = 2
		mcpServers = @(
			@{
				id = 'smoke'
				name = 'Aspire Live Smoke MCP'
				enabled = $true
				transport = 1
				command = 'node'
				arguments = @('/opt/codex-gateway/mcp-smoke-server.mjs')
				environmentVariables = @()
				availableTools = @('mcp_smoke')
			}
		)
		projects = @(
			@{
				id = 'live-smoke'
				name = 'Aspire Live Smoke'
				enabled = $true
				apiKeyAccess = @(
					@{
						apiKeyId = 'default'
						mcpServers = @(
							@{
								serverId = 'smoke'
								required = $true
								visibleTools = @('mcp_smoke')
								enabledTools = @('mcp_smoke')
							}
						)
					}
				)
			}
		)
	} | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $dataPath 'state.json') -Encoding utf8NoBOM

	$gatewayStart = [System.Diagnostics.ProcessStartInfo]::new()
	$gatewayStart.FileName = 'dotnet'
	$gatewayStart.WorkingDirectory = $repositoryRoot
	$gatewayStart.UseShellExecute = $false
	$gatewayStart.RedirectStandardOutput = $true
	$gatewayStart.RedirectStandardError = $true
	$gatewayStart.Environment['CODEX_GATEWAY_ASPIRE_DATA_PATH'] = $dataPath
	$gatewayStart.Environment['CODEX_GATEWAY_ASPIRE_CODEX_HOME_PATH'] = [System.IO.Path]::GetFullPath($CodexHomePath)
	[void] $gatewayStart.ArgumentList.Add('run')
	[void] $gatewayStart.ArgumentList.Add('--project')
	[void] $gatewayStart.ArgumentList.Add('src/CodexGateway.AppHost/CodexGateway.AppHost.csproj')
	$gatewayProcess = [System.Diagnostics.Process]::Start($gatewayStart)
	$gatewayOutput = $gatewayProcess.StandardOutput.ReadToEndAsync()
	$gatewayError = $gatewayProcess.StandardError.ReadToEndAsync()

	$baseUrl = 'http://localhost:5050'
	Wait-ForHealth $baseUrl $StartupTimeoutSeconds

	$headers = @{ Authorization = 'Bearer replace-this-api-key' }
	$models = Invoke-RestMethod -Uri "$baseUrl/v1/models" -Headers $headers -TimeoutSec $RequestTimeoutSeconds
	$selectedModel = if ([string]::IsNullOrWhiteSpace($Model)) { $models.data[0].id } else { $Model }
	if ([string]::IsNullOrWhiteSpace($selectedModel)) {
		throw 'The Codex model catalog was empty. Pass -Model after authenticating Codex.'
	}

	$eventsStart = [System.Diagnostics.ProcessStartInfo]::new()
	$eventsStart.FileName = 'docker'
	$eventsStart.UseShellExecute = $false
	$eventsStart.RedirectStandardOutput = $true
	$eventsStart.RedirectStandardError = $true
	[void] $eventsStart.ArgumentList.Add('events')
	[void] $eventsStart.ArgumentList.Add('--since')
	[void] $eventsStart.ArgumentList.Add([DateTime]::UtcNow.ToString('o'))
	[void] $eventsStart.ArgumentList.Add('--filter')
	[void] $eventsStart.ArgumentList.Add('type=container')
	[void] $eventsStart.ArgumentList.Add('--filter')
	[void] $eventsStart.ArgumentList.Add('event=create')
	[void] $eventsStart.ArgumentList.Add('--filter')
	[void] $eventsStart.ArgumentList.Add('label=com.codex-gateway.managed=true')
	[void] $eventsStart.ArgumentList.Add('--format')
	[void] $eventsStart.ArgumentList.Add('{{.Actor.ID}}')
	$eventsProcess = [System.Diagnostics.Process]::Start($eventsStart)
	$eventsOutput = $eventsProcess.StandardOutput.ReadToEndAsync()
	$eventsError = $eventsProcess.StandardError.ReadToEndAsync()

	$request = @{
		model = $selectedModel
		messages = @(
			@{
				role = 'user'
				content = 'Call the mcp_smoke tool now. Reply with exactly the text returned by the tool and no other text.'
			}
		)
	} | ConvertTo-Json -Depth 10
	$response = Invoke-RestMethod -Uri "$baseUrl/p/live-smoke/v1/chat/completions" -Method Post -Headers $headers -ContentType 'application/json' -Body $request -TimeoutSec $RequestTimeoutSeconds
	$content = $response.choices[0].message.content
	if ($content -notmatch 'MCP_SMOKE_SUCCESS') {
		throw "The live Codex response did not contain the MCP marker. Response: $content"
	}

	$eventsProcess.Kill($true)
	$eventsProcess.WaitForExit()
	$eventsProcess = $null
	$runnerContainerIds = $eventsOutput.Result.Trim()
	if ([string]::IsNullOrWhiteSpace($runnerContainerIds)) {
		throw 'The real request completed without a Docker create event for a managed runner container.'
	}

	$succeeded = $true
	Write-Output "Aspire live MCP smoke test passed with model '$selectedModel'. Managed runner container(s): $runnerContainerIds"
}
finally {
	if ($eventsProcess -and -not $eventsProcess.HasExited) {
		$eventsProcess.Kill($true)
		$eventsProcess.WaitForExit()
	}

	if ($gatewayProcess -and -not $gatewayProcess.HasExited) {
		$gatewayProcess.Kill($true)
		$gatewayProcess.WaitForExit()
	}

	if ($gatewayProcess) {
		if (-not $succeeded -and $gatewayProcess.ExitCode -ne 0 -and $gatewayError) {
			Write-Host "Aspire gateway stderr: $($gatewayError.Result)"
			Write-Host "Aspire gateway stdout: $($gatewayOutput.Result)"
		}

		$gatewayProcess.Dispose()
	}

	if (Test-Path -LiteralPath $dataPath) {
		Remove-Item -LiteralPath $dataPath -Recurse -Force
	}
}
