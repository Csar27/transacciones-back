<#
.SYNOPSIS
    Comprueba las reglas de capas leyendo los .csproj.

.DESCRIPTION
    Las reglas de capas son solo una convencion hasta que algo las verifica.
    Este script las convierte en un fallo de build: si alguien anade un
    ProjectReference de Data a Services, esto falla.

    Es mas fiable que confiar en la revision, porque la referencia se puede
    anadir sin que nadie se de cuenta al pegar un using.

.EXAMPLE
    .\validar-capas.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$raiz = $PSScriptRoot   # el script vive en la raiz del repo
Set-Location $raiz

# Cada proyecto y las referencias permitidas. Ausente = cero referencias.
$reglas = [ordered]@{
    'Transacciones.CrossCutting' = @()
    'Transacciones.Core'         = @('Transacciones.CrossCutting')
    'Transacciones.Services'     = @('Transacciones.Core', 'Transacciones.CrossCutting')
    'Transacciones.Data'         = @('Transacciones.Core', 'Transacciones.CrossCutting')
    'Transacciones.External'     = @('Transacciones.Core', 'Transacciones.CrossCutting')
    'Transacciones.Func'         = @('Transacciones.Core', 'Transacciones.CrossCutting')
    'Transacciones.WebApi'       = @('Transacciones.Core', 'Transacciones.Services',
                                     'Transacciones.Data', 'Transacciones.External',
                                     'Transacciones.CrossCutting')
    'Transacciones.Tests'        = @('Transacciones.Core', 'Transacciones.Services',
                                     'Transacciones.Data', 'Transacciones.External',
                                     'Transacciones.CrossCutting', 'Transacciones.WebApi')
}

# Projectos que NUNCA deben referenciar a otro.
$prohibidos = @{
    'Transacciones.CrossCutting' = @('Transacciones.Core', 'Transacciones.Services',
                                     'Transacciones.Data', 'Transacciones.External',
                                     'Transacciones.WebApi')
    'Transacciones.Services'     = @('Transacciones.Data')
    'Transacciones.Core'         = @('Transacciones.Services', 'Transacciones.Data',
                                     'Transacciones.External', 'Transacciones.WebApi')
}

$fallos = 0

function Referencias ([string] $proyecto) {
    $csproj = Join-Path $raiz "$proyecto\$proyecto.csproj"
    if (-not (Test-Path $csproj)) { return @() }

    [xml] $xml = Get-Content $csproj -Raw

    # Los .csproj declaran el namespace de MSBuild, y el adaptador XML de
    # PowerShell no lo atraviesa: $xml.Project.ItemGroup.ProjectReference
    # devuelve NADA (ni error). Por eso se usa XPath con local-name().
    $nodos = $xml.SelectNodes("//*[local-name()='ProjectReference']")

    $nodos |
        ForEach-Object {
            $nombre = Split-Path $_.Include -Leaf
            [System.IO.Path]::GetFileNameWithoutExtension($nombre)
        } |
        Where-Object { $_ } |
        Sort-Object -Unique
}

foreach ($proyecto in $reglas.Keys) {
    # Con StrictMode, un array de un solo elemento se desenvuelve a escalar y
    # .Count deja de existir. @() lo envuelve siempre.
    $permitidas = @($reglas[$proyecto])
    $actuales = @(Referencias $proyecto)

    Write-Host "`n$proyecto" -ForegroundColor White -NoNewline
    Write-Host "  ($($actuales.Count) referencia(s))" -ForegroundColor DarkGray

    foreach ($ref in $actuales) {
        $ok = $permitidas -contains $ref
        $marca = if ($ok) { '  OK ' } else { ' FALLA' }
        $color = if ($ok) { 'DarkGreen' } else { 'Red' }
        Write-Host "  [$marca] -> $ref" -ForegroundColor $color
        if (-not $ok) { $script:fallos++ }
    }

    # ComprobacionesDirectory: ninguna referencia prohibida.
    if ($prohibidos.ContainsKey($proyecto)) {
        foreach ($prohibido in $prohibidos[$proyecto]) {
            if ($actuales -contains $prohibido) {
                Write-Host "  [FALLA] $proyecto no debe referenciar a $prohibido" -ForegroundColor Red
                $script:fallos++
            }
        }
    }
}

# El proyecto SQL no debe aparecer como ProjectReference en ningun sitio.
Write-Host "`nProyecto SQL" -ForegroundColor White -NoNewline
Write-Host '  (nadie lo referencia)' -ForegroundColor DarkGray

foreach ($proyecto in $reglas.Keys) {
    $actuales = Referencias $proyecto

    if ($actuales | Where-Object { $_ -like 'Transacciones.Db*' }) {
        Write-Host "  [FALLA] $proyecto referencia el proyecto SQL" -ForegroundColor Red
        $fallos++
    }
}

Write-Host ''

if ($fallos -eq 0) {
    Write-Host 'Capas correctas.' -ForegroundColor Green
    exit 0
}

Write-Host "$fallos infraccion(es) de capa." -ForegroundColor Red
exit 1