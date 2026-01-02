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
  
  # Identify the registry
  $registryHost = $Env:REGISTRY_PATH;
  if ($registryHost -and $registryHost -match '^((?:[a-zA-Z0-9-]+\.)+[a-zA-Z]{2,})') {
      $registryHost = $matches[1];
  }

  Write-Output "Remote registry login: $($Env:REGISTRY_USER)@$($registryHost)";

  docker login "$($registryHost)" -u="$($Env:REGISTRY_USER)" -p="$($Env:REGISTRY_PWD)"
  ThrowIfError
}

$Env:IMAGE = "$($ENV:REGISTRY_PATH)/$($Env:IMAGE_NAME)" 

# Ensure we are in LINUX containers
if (-not(Test-Path $Env:ProgramFiles\Docker\Docker\DockerCli.exe)) {
  Get-Command docker
  Write-Warning "Docker cli not found at $Env:ProgramFiles\Docker\Docker\DockerCli.exe"
}
else {
  Write-Warning "Switching to Linux Engine"
  & $Env:ProgramFiles\Docker\Docker\DockerCli.exe -SwitchLinuxEngine
}

if ((Test-Path "env-private.env") -eq $false) {
    New-Item "env-private.env" -type file
}

echo "Starting Docker Compose Build with no cache"
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
