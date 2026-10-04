<#
.SYNOPSIS
    Cambia la configuracion entre entornos sin tocar appsettings.json.

.DESCRIPTION
    appsettings.json guarda valores de desarrollo y NUNCA secretos de
    produccion. Este script mantiene los valores por entorno en archivos
    fuera del control de versiones (entornos.local.json) y los copia encima
    antes de arrancar.

    Es una separacion deliberada: si el fichero de produccion viviera en el
    repositorio, la cadena de conexion estaria en el historial de git para
    siempre, aunque luego la borres del fichero.

.PARAMETER Entorno
    demo | uat | prod

.EXAMPLE
    .\switch.entorno.ps1 -Entorno uat
    .\switch.entorno.ps1 -Entorno prod -Verificar
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('demo', 'uat', 'prod')]
    [string] $Entorno,

    [switch] $Verificar
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$raiz = $PSScriptRoot   # el script vive en la raiz del repo
Set-Location $raiz

$rutaEntornos = Join-Path $raiz 'entornos.local.json'

if (-not (Test-Path $rutaEntornos)) {
    Write-Host @"
No existe entornos.local.json.

Crealo con la estructura:

  {
    "demo": { "ABCMultiSettings": { "ConnectionStringAff": "...", "ConnectionStringSme": "..." } },
    "uat":  { "ABCMultiSettings": { "ConnectionStringAff": "...", "ConnectionStringSme": "..." } },
    "prod": { "ABCMultiSettings": { "ConnectionStringAff": "...", "ConnectionStringSme": "..." } }
  }

Anadelo a .gitignore. Ese fichero nunca debe subirse.
"@ -ForegroundColor Yellow
    exit 1
}

$entornos = Get-Content $rutaEntornos -Raw -Encoding UTF8 | ConvertFrom-Json

if (-not $entornos.PSObject.Properties.Name.Contains($Entorno)) {
    Write-Host "El entorno '$Entorno' no esta en entornos.local.json. Disponibles:" -ForegroundColor Red
    $entornos.PSObject.Properties.Name | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

$configuracion = $entornos.$Entorno

if ($Verificar) {
    Write-Host "Entorno: $Entorno" -ForegroundColor Cyan

    # Comprobar SOLO que existen. Imprimir una cadena de conexion en una
    # consola compartida es filtrarla.
    $secciones = @('ABCMultiSettings')
    $faltan = @()

    foreach ($seccion in $secciones) {
        $valor = $configuracion.$seccion
        if ($null -eq $valor) {
            $faltan += $seccion
            continue
        }

        foreach ($prop in $valor.PSObject.Properties) {
            if ([string]::IsNullOrWhiteSpace($prop.Value)) {
                $faltan += "$seccion`:$($prop.Name)"
            }
        }
    }

    if ($faltan.Count -gt 0) {
        Write-Host 'Valores vacios:' -ForegroundColor Red
        $faltan | ForEach-Object { Write-Host "  - $_" }
        exit 1
    }

    Write-Host 'Configuracion completa.' -ForegroundColor Green
    exit 0
}

$rutaAppSettings = Join-Path $raiz 'Transacciones.WebApi\appsettings.json'
$copia = "$rutaAppSettings.bak"

Copy-Item $rutaAppSettings $copia -Force

try {
    $actual = Get-Content $rutaAppSettings -Raw -Encoding UTF8 | ConvertFrom-Json

    foreach ($prop in $configuracion.PSObject.Properties) {
        if ($null -eq $actual.PSObject.Properties[$prop.Name]) {
            $actual | Add-Member -NotePropertyName $prop.Name -NotePropertyValue $prop.Value
            continue
        }

        foreach ($interno in $prop.Value.PSObject.Properties) {
            $actual.($prop.Name).($interno.Name) = $interno.Value
        }
    }

    $actual | ConvertTo-Json -Depth 10 |
        Set-Content $rutaAppSettings -Encoding UTF8

    Write-Host "Entorno '$Entorno' aplicado." -ForegroundColor Green
    Write-Host "Copia del anterior en: $copia" -ForegroundColor DarkGray
}
catch {
    # Si algo falla a medias, appsettings.json queda inconsistente. Restaurarlo
    # siempre es mejor que dejarlo a medias sin avisar.
    Move-Item $copia $rutaAppSettings -Force
    Write-Host 'Se produjo un error; appsettings.json ha sido restaurado.' -ForegroundColor Red
    throw
}