param (
    [switch]$Push = $false,
    [switch]$StartContainers = $false
)

$global:ErrorActionPreference = 'Stop'

. .\helpers.ps1

if (Test-Path "envsettings.ps1") {
  .\envsettings.ps1;
}

if ($Env:REGISTRY_USER -and $Env:REGISTRY_PWD) {
  Write-Output "Container registry credentials through environment provided."
  
  # Identify the registry - Docker Hub or custom registry
  $registryHost = $Env:REGISTRY_PATH;
  if ($registryHost -and $registryHost -match '^((?:[a-zA-Z0-9-]+\.)+[a-zA-Z]{2,})') {
      # Custom registry (e.g., registry.azurecr.io)
      $registryHost = $matches[1];
      Write-Output "Remote registry login: $($Env:REGISTRY_USER)@$($registryHost)";
      docker login "$($registryHost)" -u="$($Env:REGISTRY_USER)" -p="$($Env:REGISTRY_PWD)"
  } else {
      # Docker Hub (no domain, just username/repo)
      Write-Output "Docker Hub login: $($Env:REGISTRY_USER)";
      docker login -u="$($Env:REGISTRY_USER)" -p="$($Env:REGISTRY_PWD)"
  }
  ThrowIfError
}

# Set image name - use registry path if provided, otherwise use local image name
if ($Env:REGISTRY_PATH) {
    $Env:IMAGE = "$($ENV:REGISTRY_PATH)/$($Env:IMAGE_NAME)"
} else {
    $Env:IMAGE = $Env:IMAGE_NAME
}
Write-Output "Docker image name: $($Env:IMAGE)" 

# Force Linux platform
$env:DOCKER_DEFAULT_PLATFORM = "linux/amd64"

# Ensure we are in LINUX containers
if (Test-Path $Env:ProgramFiles\Docker\Docker\DockerCli.exe) {
  Write-Output "Switching to Linux Engine"
  & $Env:ProgramFiles\Docker\Docker\DockerCli.exe -SwitchLinuxEngine
  Start-Sleep -Seconds 2
}

if ((Test-Path "env-private.env") -eq $false) {
    New-Item "env-private.env" -type file
}

Write-Output "Starting Docker Compose Build with no cache (Linux platform)"
docker compose -f compose.yaml build
ThrowIfError

if ($StartContainers -eq $true) {
  docker compose -f compose.yaml up
  ThrowIfError
}

if ($push) { 
  docker push "$($Env:IMAGE)" 
  ThrowIfError
}
