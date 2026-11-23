<#
.SYNOPSIS
  Gera a versão publicável do Mercado no Controle: API .NET + interface Angular em uma única pasta.

.DESCRIPTION
  1. Compila o Angular em modo produção.
  2. Publica a API (Release) na pasta de saída.
  3. Copia a interface compilada para <saída>/wwwroot, servida pela própria API.

  Para executar depois:
    cd <saída>
    $env:ASPNETCORE_URLS = "http://localhost:5080"
    dotnet MercadoNoControle.Api.dll

  O banco e as imagens ficam em <saída>/data e <saída>/uploads (appsettings.Production.json),
  a menos que Storage__DatabasePath / Storage__UploadsPath apontem para outro local — recomendado,
  para que uma nova publicação não substitua os dados.

.PARAMETER Output
  Pasta de saída (padrão: publish, na raiz do repositório).

.PARAMETER SkipInstall
  Não executa "npm ci" (usa o node_modules existente).
#>
param(
    [string]$Output = "publish",
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$frontend = Join-Path $root "frontend/mercado-no-controle"
$api = Join-Path $root "backend/MercadoNoControle.Api"
$out = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }

Write-Host "==> Interface Angular" -ForegroundColor Cyan
Push-Location $frontend
try {
    if (-not $SkipInstall) { npm ci; if ($LASTEXITCODE -ne 0) { throw "npm ci falhou." } }
    npx ng build --configuration production
    if ($LASTEXITCODE -ne 0) { throw "Build do Angular falhou." }
}
finally { Pop-Location }

Write-Host "==> API .NET" -ForegroundColor Cyan
dotnet publish $api --configuration Release --output $out
if ($LASTEXITCODE -ne 0) { throw "dotnet publish falhou." }

Write-Host "==> Copiando a interface para wwwroot" -ForegroundColor Cyan
$wwwroot = Join-Path $out "wwwroot"
if (Test-Path $wwwroot) { Remove-Item $wwwroot -Recurse -Force }
New-Item -ItemType Directory -Force $wwwroot | Out-Null
Copy-Item (Join-Path $frontend "dist/mercado-no-controle/browser/*") $wwwroot -Recurse

Write-Host "Publicação gerada em $out" -ForegroundColor Green
