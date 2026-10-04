<#
.SYNOPSIS
    Compila, prueba y publica la solucion Transacciones.

.DESCRIPTION
    Envoltura de los comandos que se ejecutan siempre igual. El objetivo es que
    la secuencia sea la misma para todos: compilar antes de probar, probar antes
    de publicar. Publicar sin probar es como desplegar sin mirar los espejos.

.PARAMETER Entorno
    demo | uat | prod. Solo afecta a la configuracion; la publicacion no escribe
    en ningun servidor.

.PARAMETER SaltarPruebas
    Util solo para iterar en local. En un entorno compartido, no.

.EXAMPLE
    .\deploy.demo.ps1
    .\deploy.prod.ps1 -SaltarPruebas
#>
[CmdletBinding()]
param(
    [ValidateSet('demo', 'uat', 'prod')]
    [string] $Entorno = 'demo',

    [switch] $SaltarPruebas
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$raiz = $PSScriptRoot   # el script vive en la raiz del repo
Set-Location $raiz

function Escribir ([string] $mensaje, [string] $color = 'Gray') {
    Write-Host "`n==> $mensaje" -ForegroundColor $color
}

Escribir "Restaurando paquetes"
dotnet restore Transacciones.sln
if ($LASTEXITCODE -ne 0) { throw 'Falló el restore.' }

Escribir "Compilando en Release" 'Cyan'
dotnet build Transacciones.sln --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Falló el build.' }

if (-not $SaltarPruebas) {
    Escribir 'Ejecutando pruebas' 'Cyan'
    dotnet test Transacciones.Tests\Transacciones.Tests.csproj `
        --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas. No se publica.' }
}

Escribir "Publicando Web API ($Entorno)" 'Cyan'
dotnet publish Transacciones.WebApi\Transacciones.WebApi.csproj `
    --configuration Release --no-build --output ".\publish\webapi"
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación de la Web API.' }

Escribir 'Publicando Function App' 'Cyan'
dotnet publish Transacciones.Func\Transacciones.Func.csproj `
    --configuration Release --no-build --output ".\publish\func"
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación del Function App.' }

Escribir "Listo. Artefactos en .\publish\" 'Green'
Write-Host @'

El proyecto SQL (Transacciones.Db) NO se publica con dotnet: necesita SSDT.
Para aplicar el esquema en un entorno, usa:

  sqlcmd -S <servidor> -d <base> -E -b -i Transacciones.Db\deploy.sql

O abre Transacciones.Db.sqlproj en Visual Studio y despliega con SSDT.

'@ -ForegroundColor DarkGray