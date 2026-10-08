[CmdletBinding()]
param(
    [string] $GatewayImage = 'codex-gateway:latest',
    [string] $RunnerImage = 'codex-gateway-runner:latest',
    [switch] $Push
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Assert-NativeSuccess([string] $Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step failed with exit code $LASTEXITCODE."
    }
}

# Resolve once per release. The changing build argument invalidates Docker's
# npm layer, and both images receive the same current stable CLI.
$codexVersion = (Invoke-RestMethod 'https://registry.npmjs.org/@openai%2fcodex/latest').version
if ($codexVersion -notmatch '^\d+\.\d+\.\d+$') {
    throw "The npm latest tag did not resolve to a stable Codex CLI version."
}
Write-Output "Building Gateway and runner with Codex CLI $codexVersion."

foreach ($image in @(
    @{ Target = 'runner'; Tag = $RunnerImage },
    @{ Target = 'gateway'; Tag = $GatewayImage }
)) {
    & docker build --pull --target $image.Target --tag $image.Tag --build-arg "CODEX_VERSION=$codexVersion" $repositoryRoot
    Assert-NativeSuccess "$($image.Target) image build"
    $actualVersion = & docker run --rm --entrypoint codex $image.Tag --version
    Assert-NativeSuccess "$($image.Target) CLI version check"
    if ($actualVersion.Trim() -ne "codex-cli $codexVersion") {
        throw "$($image.Target) has unexpected CLI version: $actualVersion"
    }
}

& (Join-Path $PSScriptRoot 'verify-container.ps1') -SkipBuild -GatewayImage $GatewayImage -RunnerImage $RunnerImage

if ($Push) {
    foreach ($tag in @($RunnerImage, $GatewayImage)) {
        & docker push $tag
        Assert-NativeSuccess "Push $tag"
    }
}
