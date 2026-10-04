# Transacciones — API

Gestión de solicitudes de transacciones: alta de solicitudes con monto y
vigencia, y su ciclo de vida hasta un estado final.

.NET 8 por capas, EF Core sobre SQL Server,Azure Functions para el trabajo en
segundo plano. La interfaz web es un repositorio aparte
([`transacciones-front`](https://github.com/Csar27/transacciones-front)); esta
API no depende de ella para compilar ni para arrancar.

---

## Puesta en marcha

### Requisitos

- .NET SDK 8
- SQL Server Express en `(local)\SQLEXPRESS`
- Para la Function, los Azure Functions Core Tools — no forman parte de este
  repositorio

### Base de datos

El esquema completo está en [`Transacciones.Db/deploy.sql`](Transacciones.Db/deploy.sql)
y se aplica sin Visual Studio:

```powershell
sqlcmd -S "(local)\SQLEXPRESS" -d TransaccionesDb -E -b -i .\Transacciones.Db\deploy.sql
```

> Ese script **borra las tablas** antes de crearlas. Es para desarrollo.

### Arrancar

```powershell
dotnet build Transacciones.sln -c Release
dotnet run --project Transacciones.WebApi\Transacciones.WebApi.csproj -c Release
```

| | |
|---|---|
API | **http://localhost:5250** |
Swagger | **http://localhost:5250/swagger** |

El puerto viene de `Kestrel:Endpoints:Http:Url` en `appsettings.json`, no del
`launchSettings.json`.

---

## Estructura

Ocho proyectos en la solución, más el de esquema SQL que va aparte.

```
Transacciones.Core           Modelo de dominio. Sin dependencias de nada
Transacciones.CrossCutting   Enums, sobre de respuesta, middlewares, DI
Transacciones.Services       Casos de uso. Reglas de negocio
Transacciones.Data           EF Core, DbContext, configuraciones, repositorios
Transacciones.External       Clientes HTTP de Notificaciones y Catálogo
Transacciones.WebApi         Controllers, DI, middleware, Quartz  ← Microsoft.NET.Sdk.Web
Transacciones.Func           Azure Functions, worker aislado           ← Microsoft.NET.Sdk
Transacciones.Tests          xUnit
Transacciones.Db             Esquema SQL (SSDT) — fuera del .sln
```

**`Transacciones.Func` usa `Microsoft.NET.Sdk`, no `Sdk.Web`.** A propósito: un
proyecto web registrado dos veces en la misma solución hace que Visual Studio
choque con `SeIHostWebServer` al abrirla.

---

## Reglas de capas

Las capas son una convención hasta que algo las verifica.
[`validar-capas.ps1`](validar-capas.ps1) las convierte en un fallo, leyendo los
`.csproj`. Es más fiable que confiar en la revisión, porque la referencia se
puede añadir sin que nadie se dé cuenta al pegar un `using`.

| Proyecto | Puede referenciar |
|---|---|
`CrossCutting` | — |
`Core` | `CrossCutting` |
`Services` | `Core`, `CrossCutting` |
`Data` | `Core`, `CrossCutting` |
`External` | `Core`, `CrossCutting` |
`Func` | `Core`, `CrossCutting` |
`WebApi` | `Core`, `Services`, `Data`, `External`, `CrossCutting` |
`Tests` | todos |

Y lo que nunca debe ocurrir: `Services → Data`, `Core → Services|Data|External|WebApi`,
`CrossCutting → nada del resto`. **Nadie** referencia a `Transacciones.Db`: el
esquema no se consume desde código.

```powershell
.\validar-capas.ps1    # sale con código 1 si alguien rompe el reparto
```

---

## Compilar y probar

```powershell
dotnet build Transacciones.sln -c Release
dotnet test Transacciones.Tests\Transacciones.Tests.csproj
```

### Las 81 pruebas

63 métodos que se expanden a **81 casos**: 55 `[Fact]` y 8 `[Theory]` con 26
`[InlineData]`.

| Fichero | Casos | Qué cubre |
|---|---|---|
`Modelos/SolicitudTests.cs` | 22 | Transiciones, motivos, vencimiento |
`Controllers/SolicitudControllerTests.cs` | 8 | Cada endpoint con el servicio simulado |
`Services/SolicitudServiceTests.cs` | 11 | Validación de vigencia, códigos |
`InyeccionDependenciasTests.cs` | 7 | Que todo esté registrado |
`Middleware/HeaderMiddlewareTests.cs` | 15 | Lectura de `X_isMultiRisk` |
`ArranqueApiTests.cs` | 18 | **La API de verdad, por HTTP** |

### Por qué importa `ArranqueApiTests`

**Los tests con Moq no detectan un fallo de escritura en base de datos.** Si EF
genera un `UPDATE` inválido, los unitarios pasan todos porque nunca tocan la
base. Y como el middleware responde un `GEN-500` genérico a propósito, desde el
navegador un fallo de guardado es indistinguible de cualquier otro.

Por eso las aserciones de estos tests llevan el cuerpo real de la respuesta en
el mensaje de fallo:

```csharp
respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await MotivoAsync(respuesta));
```

Si uno falla, el mensaje dice qué devolvió la API en lugar de solo
`Expected 200, got 500`.

---

## La API

Base: `/api/solicitudes`

| Método | Ruta | Notas |
|---|---|---|
`GET` | `/` | Paginado. Filtros: `codigo`, `clienteId`, `estado`, `desde`, `hasta` |
`GET` | `/{id}` | Ruta **con nombre**: `obtener-solicitud` |
`POST` | `/` | `201` con `Location`. Nace en `Registrada` |
`PUT` | `/{id}/aprobar` | |
`PUT` | `/{id}/rechazar?motivo=` | Motivo obligatorio |
`PUT` | `/{id}/anular` | |
`GET` | `/correlacion` | Id de correlación de la petición en curso |

**Las transiciones son `PUT`, no `PATCH`.** Son idempotentes —aprobar dos veces
una solicitud aprobada no la deshace— y el verbo correcto es el que sustituye
el estado completo del recurso.

### El sobre de respuesta

Todo va envuelto en `ApiResponse<T>`
([`Transacciones.CrossCutting/Model/ApiResponse.cs`](Transacciones.CrossCutting/Model/ApiResponse.cs)):

```json
{
  "exito": true,
  "datos": { },
  "codigo": null,
  "mensaje": null,
  "correlationId": "..."
}
```

En error, `exito` vale `false`, `datos` es `null` y `codigo` trae el código de
negocio (`SOL-003`, `SOL-004`, …). `correlationId` se rellena siempre: es lo que
el usuario reporta cuando algo falla.

> El cliente tiene que saber manejar **dos** formas: este sobre y `ProblemDetails`,
> que es lo que devuelve la validación de modelo de `[ApiController]`. El
> interceptor de Axios normaliza las dos.

### Estados y transiciones

El valor numérico **es parte del contrato**: se persiste, así que reordenar el
enum rompe datos existentes.

| | Estado | Terminal | Vencenrible |
|---|---|---|---|
`0` | Borrador | | ✓ |
`1` | Registrada | | ✓ |
`2` | Aprobada | | |
`3` | Rechazada | ✓ | |
`4` | Vencida | ✓ | |
`5` | Anulada | ✓ | |

Transiciones permitidas —todo lo demás es `SOL-003`—:

```
Borrador    → Registrada, Anulada
Registrada  → Aprobada, Rechazada, Vencida, Anulada
Aprobada    → Anulada
```

**"Terminal" y "vencenible" no son lo mismo.** Una `Aprobada` sigue abierta —
se puede anular—, pero el job de vencimiento no la toca. Por eso
`EstadosVencenables` es una lista explícita `{ Borrador, Registrada }` y no la
negación de `EstadosTerminales`.

---

## Configuración

### Las dos cabeceras

| Cabecera | Efecto |
|---|---|
`X_isMultiRisk: true` | Enruta la petición a la base SME en vez de a la normal |
`X_userId` | Se escribe en `creadoPor` |

Acepta `true`, `True`, `TRUE` y `1`; cualquier otra cosa —incluido `false`, `0`,
vacío y basura— cuenta como no-SME.

### Las dos connection strings

`ABCMultiSettings:ConnectionStringAff` y `:ConnectionStringSme` **deben existir
las dos**. Es deliberado: un arranque fallido en el despliegue se ve, una
petición que escribe en la base equivocada no.

### Servicios externos

`ExternalApis:NotificacionesUrl` y `:CatalogoUrl`. **Si una URL queda vacía, el
cliente no se registra** en vez de romper el arranque.

---

## Decisiones que conviene conocer

### `OUTPUT INTO` y el trigger

EF Core usa cláusulas `OUTPUT` para leer el valor generado sin volver a
consultar. Con triggers, el motor exige:

```csharp
modelBuilder.ToTable("Solicitud", "dbo", t => t.HasTrigger("trg_Solicitud_Auditoria"));
```

**No hay ningún trigger sobre `dbo.Solicitud`, y es deliberado.** Hubo uno que
estampaba la auditoría con un `UPDATE` sobre la misma tabla que lo había
disparado. Eso solo funciona mientras `RECURSIVE_TRIGGERS` esté apagado; en
cuanto alguien lo activa, el trigger se dispara sobre sí mismo y agota el
límite de anidamiento.

El fallo salía **solo al actualizar, nunca al insertar**: crear solicitudes
funcionaba, aprobar no, y el cliente recibía un 500 sin detalle.

La auditoría la estampa ahora `SolicitudService.MarcarModificada()`. La
declaración `HasTrigger` sigue en su sitio porque `OUTPUT INTO` la necesita
haya trigger o no — y hace que un `DROP` previo no sea obligatorio.

### `CreatedAtRoute`, no `CreatedAtAction`

La ruta de `GET /{id}` lleva `Name = "obtener-solicitud"`. Con *attribute
routing*, `CreatedAtAction` por nombre de acción no siempre resuelve: si no,
el `POST` devuelve **500 al construir la cabecera `Location`, después de haber
guardado la solicitud**. Datos huérfanos y error al usuario.

### NLog con ruta explícita

```csharp
builder.Logging.AddNLog("nlog.config");
builder.Logging.AddConsole();
```

`AddNLogWeb()` sin argumentos no localiza el `nlog.config` en este montaje: NLog
arranca sin ningún target y no escribe ni en consola ni en fichero, **sin
avisar**. Un logger que no registra nada es peor que no tener logger, porque
aparece que funciona y se pierde justo lo que hace falta para depurar. La
consola es la red.

### Polly: esperas de sub-segundo

Reintentos con esperas de **200 / 400 / 800 ms**, no de 2/4/8 segundos. Y el
timeout del cliente de notificaciones es de **3 s**: con 30 s × 3 reintentos el
cliente HTTP se rendía antes que la propia API.

### `CORS` con lista blanca

`Cors:Origenes` con `http://localhost:4200` (Angular) y
`http://localhost:5173` (Vite). Sin configurarla, el middleware cierra el
acceso; no se abre por omisión.

---

## Qué no incluye

**El catálogo de clientes no está conectado.** `CatalogoService` está registrado
en DI con su `HttpClient`, su timeout y sus reintentos… y **nadie lo inyecta**.
No hay interfaz `ICatalogoService` ni consumidor.

Es la pieza que falta: sin ella, el `clienteId` de una solicitud nueva no se
puede elegir de una lista real. Mientras tanto, el cliente usa un catálogo de
ejemplo en su propio repositorio.

No es un descuido de arranque —`AddHttpClient` sin interfaz registra la clase
concreta y compila sinwarnings—, es una pieza sin terminar.

Tampoco hay autenticación. El interceptor de Axios ya tiene el hueco listo
(`definirProveedorDeToken`), pero ninguna petición lleva `Authorization`.

---

## El proyecto SQL

`Transacciones.Db` es un proyecto SSDT y **no está en la solución**: no es un
`.csproj`.

Hay **dos caminos** con el mismo esquema:

- El **proyecto SSDT**, fuente de verdad para el modelo y los diffs
- **`deploy.sql`**, el esquema entero en un script, para cuando no hay Visual
  Studio

Si cambias un objeto, cambia los dos. Es duplicación incómoda, pero es lo que
permite aplicar el esquema desde una línea de comandos.

El correlativo de códigos usa `SEQUENCE`, no `MAX(Codigo) + 1`: dos peticiones
concurrentes leerían el mismo máximo y generarían el mismo código. Al final de
`deploy.sql` hay un bloque que **resincroniza la secuencia con los datos que ya
hubiera** — sin él, sobre una base con solicitudes, el primer `POST` chocaría
contra el índice único con un 409.

---

## Convenciones

- Español en identificadores, comentarios y literales. Sin excepciones.
- El controller es delgado: traduce HTTP a llamadas del servicio. **Si aparece
  una regla de negocio en un controller, está en el sitio equivocado.**
- Los puntos de control de transiciones viven en un solo sitio
  (`Solicitud.Transicionar`), no repartidos por los servicios.
- Un solo código de respuesta por familia de error. Nada de `200` con cuerpo de
  error: es la forma habitual de que un cliente ignore un fallo.
- Nada de `Console.WriteLine`. Los scripts PowerShell usan `$PSScriptRoot` para
  localizarse, así que funcionan desde cualquier carpeta.