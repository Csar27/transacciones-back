<#
.SYNOPSIS
    Mueve el backend de .NET a la carpeta backend\ y deja frontend\ al lado.

.DESCRIPTION
    Transacciones\, al principio, tenia los nueve proyectos .NET y el frontend
    mezclados en la raiz. Este script hace el reparto:

        Transacciones\
        |-- backend\    <- 9 proyectos, la solucion, los scripts, YAML
        |-- frontend\   <- la aplicacion React (ya esta aqui)
        +-- README.md

    Lo delicate es lo que NO hay que tocar:

    - Transacciones.sln usa rutas RELATIVAS. Si se mueve la solucion junto con
      los proyectos, sigue funcionando sin editarla.
    - validar-capas.ps1, deploy.demo.ps1 y switch.entorno.ps1 usan $PSScriptRoot.
      Si se mueven con los proyectos, siguen funcionando sin editarlos.

    Por eso el script mueve cada elemento DESPUES de borrar bin\, obj\ y .vs\:
    esos artefactos guardan rutas absolutas a D:\projects\Transacciones\... y,
    si se quedan, MSBuild puede arrastrar la ubicacion antigua. El manifest de
    pruebas (MvcTestingAppManifest.json) tambien es absoluto y lo regenera el
    build, asi que borrar antes es lo que hace que ArranqueApiTests siga
    encontrando el content root.

.NOTES
    README.md NO se mueve: ya esta escrito en backend\README.md, actualizado.
    Borra los artefactos de compilacion, que se regeneran. No toca la base de
    datos ni el contenido de los proyectos.

.EXAMPLE
    .\mover-backend.ps1
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param()

$ErrorActionPreference = 'Stop'

$raiz = $PSScriptRoot
$destino = Join-Path $raiz 'backend'

# --- Comprobaciones previas -------------------------------------------------

if (-not $raiz) {
    throw 'No se pudo resolver la carpeta del script. Ejecutalo con su ruta completa.'
}

$solucion = Join-Path $destino 'Transacciones.sln'

if (Test-Path $solucion) {
    Write-Host 'La migracion ya se hizo: backend\Transacciones.sln existe.' -ForegroundColor Yellow
    Write-Host 'No hay nada que mover. Puedes borrar este script.' -ForegroundColor Yellow
    exit 1
}

# Carpetas que se mueven. La lista es explicita a proposito: mover "todo lo que
# hay aqui" arrastraria despues frontend\ y este mismo script dentro de backend.
$PROYECTOS = @(
    'Transacciones.Core'
    'Transacciones.CrossCutting'
    'Transacciones.Data'
    'Transacciones.Db'
    'Transacciones.External'
    'Transacciones.Func'
    'Transacciones.Services'
    'Transacciones.Tests'
    'Transacciones.WebApi'
)

# .gitignore NO se mueve, a proposito.
#
# Este repositorio va a ser un monorepo: backend\ de .NET y frontend\ de npm.
# Un .gitignore dentro de backend\ no cubre frontend\, asi que al moverlo la
# raiz se quedaria sin ninguno y frontend\node_modules —cientos de miles de
# ficheros— acabaria en el repositorio.
#
# El de la raiz se queda donde esta y cubre las dos mitades.
$ARCHIVOS = @(
    'Transacciones.sln'
    'validar-capas.ps1'
    'deploy.demo.ps1'
    'switch.entorno.ps1'
)

$CARPETAS = @('YAML')

$movibles = @($PROYECTOS + $ARCHIVOS + $CARPETAS | Where-Object { Test-Path (Join-Path $raiz $_) })

if ($movibles.Count -eq 0) {
    Write-Host "No hay nada que mover en $raiz" -ForegroundColor Yellow
    exit 1
}

Write-Host ''
Write-Host 'Se va a mover a backend\:' -ForegroundColor Cyan
$movibles | ForEach-Object { Write-Host "  $_" }
Write-Host ''

if (-not $PSCmdlet.ShouldProcess($destino, 'Mover el backend')) {
    Write-Host 'Cancelado.' -ForegroundColor Yellow
    exit 0
}

# --- 1. Limpiar artefactos con rutas absolutas ------------------------------

Write-Host '1. Borrando artefactos de compilacion...' -ForegroundColor Cyan

$limpieza = 0

foreach ($proyecto in $PROYECTOS) {
    $rutaProyecto = Join-Path $raiz $proyecto

    if (-not (Test-Path $rutaProyecto)) { continue }

    # .vs es cache de Visual Studio y guarda rutas absolutas de la maquina.
    $vs = Join-Path $rutaProyecto '.vs'

    if (Test-Path $vs) {
        Remove-Item $vs -Recurse -Force
        $limpieza++
    }

    # bin y obj se regeneran con el build. Conservarlos solo sirve para arrastrar
    # rutas antiguas.
    foreach ($carpeta in @('bin', 'obj')) {
        $ruta = Join-Path $rutaProyecto $carpeta

        if (Test-Path $ruta) {
            Remove-Item $ruta -Recurse -Force
            $limpieza++
        }
    }
}

Write-Host "   $limpieza carpetas de artefactos borradas." -ForegroundColor DarkGray

# --- 2. Mover ---------------------------------------------------------------

Write-Host '2. Moviendo a backend\' -ForegroundColor Cyan

if (-not (Test-Path $destino)) {
    New-Item -ItemType Directory -Path $destino | Out-Null
}

foreach ($elemento in $movibles) {
    $origen = Join-Path $raiz $elemento
    $final = Join-Path $destino $elemento

    Move-Item -Path $origen -Destination $final -Force
    Write-Host "   $elemento" -ForegroundColor DarkGray
}

# --- 3. Verificar -----------------------------------------------------------

Write-Host ''
Write-Host '3. Verificando...' -ForegroundColor Cyan

$fallos = @()

# 3.1 Todos los proyectos del .sln existen bajo backend\ con rutas relativas.
$rutaSln = Join-Path $destino 'Transacciones.sln'

if (-not (Test-Path $rutaSln)) {
    $fallos += 'No se encontro Transacciones.sln en backend\.'
}
else {
    $contenido = Get-Content $rutaSln

    $referencias = $contenido |
        Where-Object { $_ -match '^Project\(' } |
        ForEach-Object {
            # Formato: = "Nombre", "Ruta\al\proyecto.csproj", "{GUID}"
            if ($_ -match '"([^"]+\.csproj)"') { $Matches[1] }
        }

    foreach ($referencia in $referencias) {
        $rutaProyecto = Join-Path $destino $referencia

        if (-not (Test-Path $rutaProyecto)) {
            $fallos += "El .sln referencia $referencia y no existe en backend\."
        }
    }

    Write-Host "   $($referencias.Count) proyectos referenciados por el .sln: todos presentes." -ForegroundColor DarkGray
}

# 3.2 Los scripts siguen resolviendo su propia carpeta.
foreach ($script in @('validar-capas.ps1', 'deploy.demo.ps1', 'switch.entorno.ps1')) {
    $ruta = Join-Path $destino $script

    if (-not (Test-Path $ruta)) {
        $fallos += "Falta $script en backend\."
        continue
    }

    # Si alguien quito el $PSScriptRoot por una ruta absoluta, el script dejaria
    # de funcionar en cuanto cambie de carpeta.
    if (-not (Select-String -Path $ruta -Pattern '\$PSScriptRoot' -Quiet)) {
        $fallos += "$script ya no usa `$PSScriptRoot y puede que tenga una ruta absoluta."
    }
}

if ($fallos.Count -eq 0) {
    Write-Host '   Los tres scripts siguen resolviendo su propia carpeta.' -ForegroundColor DarkGray
}

# 3.3 Nada se quedo atras en la raiz.
Write-Host ''
Write-Host '   Contenido actual de la raiz:' -ForegroundColor DarkGray

Get-ChildItem -Path $raiz -Force |
    Where-Object { $_.Name -notlike '.*' } |
    ForEach-Object { Write-Host "     $($_.Name)" -ForegroundColor DarkGray }

# --- Resultado --------------------------------------------------------------

Write-Host ''

if ($fallos.Count -gt 0) {
    Write-Host 'La migracion se hizo, pero hay avisos:' -ForegroundColor Yellow
    $fallos | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    exit 1
}

Write-Host 'Backend movido. Ahora verifica:' -ForegroundColor Green
Write-Host ''
Write-Host '  cd D:\projects\Transacciones\backend'
Write-Host '  dotnet build Transacciones.sln -c Release' -ForegroundColor White
Write-Host '  dotnet test  Transacciones.Tests\Transacciones.Tests.csproj' -ForegroundColor White
Write-Host '  .\validar-capas.ps1' -ForegroundColor White
Write-Host ''
Write-Host 'El proyecto SQL no se valida aqui: necesita Visual Studio.' -ForegroundColor DarkGray
Write-Host 'Este script se puede borrar cuando todo este en verde.'