using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NLog.Extensions.Logging;
using Transacciones.CrossCutting.Model;
using Transacciones.Data.Persistence;
using Transacciones.WebApi.Extension;
using Transacciones.WebApi.Middleware;
using Transacciones.WebApi.Schedular;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Logging: NLog por delante, Microsoft.Extensions.Logging debajo.
// NLog se pone primero para que sus providers reciban los eventos de los demas.
// ---------------------------------------------------------------------------
builder.Logging.ClearProviders();

// El fichero de configuracion se indica EXPRESAMENTE.
//
// AddNLogWeb() sin argumentos no localiza el nlog.config en este montaje: NLog
// arranca sin ningun target y no escribe ni en consola ni en fichero, sin
// avisar. Un logger que no registra nada es peor que no tener logger, porque
// aparenta que funciona y se pierde justo lo que hace falta para depurar.
//
// La sobrecarga es AddNLog(ILoggingBuilder, string): la ruta relativa del
// fichero. No existe un AddNLog que acepte un lambda de opciones.
builder.Logging.AddNLog("nlog.config");

// Consola por si acaso. Si el fichero de NLog no estuviera, al menos la
// excepcion se ve en la terminal en vez de desaparecer en silencio.
builder.Logging.AddConsole();

// ---------------------------------------------------------------------------
// Registro por capas. Cada extension corresponde a una capa, de modo que se
// lee de un vistazo quien aporta que.
// ---------------------------------------------------------------------------
builder.Services.AddCrossCuttingReferences();
builder.Services.AddDataReferences(builder.Configuration);
builder.Services.AddApiReferences(builder.Configuration);
builder.Services.AddServices();
builder.Services.RegisterAutoMapper(builder.Configuration);
builder.Services.AddCORS(builder.Configuration);
builder.Services.AddNewtonsoftJson();
builder.Services.AddSwagger();
builder.Services.RegisterBackgroundTask();

// ---------------------------------------------------------------------------
// Controladores
// ---------------------------------------------------------------------------
builder.Services.AddControllers();

// Un solo codigo de respuesta por familia de error: evita el 200 con cuerpo
// de error, que es la forma habitual de que un cliente ignore un fallo.
builder.Services.Configure<ApiBehaviorOptions>(opciones =>
{
    opciones.SuppressModelStateInvalidFilter = false;
    opciones.InvalidModelStateResponseFactory = contexto =>
    {
        var errores = contexto.ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                e => e.Key,
                e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray());

        // "Datos" lleva el diccionario campo -> mensajes. Antes se calculaba y se
        // descartaba, asi que el 400 llegaba al front como un mensaje generico sin
        // forma de saber que campo corregir.
        //
        // Si hay un solo mensaje, ese es el que se muestra: "El monto debe estar
        // entre 0.01 y 9999999.99" es accionable; "La peticion tiene datos
        // invalidos" obliga a adivinar.
        //
        // La clave de ModelState viene en la notacion de ASP.NET ("Monto",
        // "moneda", "payload.vigenciaDias"); el front la normaliza a camelCase.
        var primerMensaje = errores.Values
            .SelectMany(mensajes => mensajes)
            .FirstOrDefault(m => !string.IsNullOrWhiteSpace(m));

        return new BadRequestObjectResult(new ApiResponse<object>
        {
            Exito = false,
            Codigo = "GEN-400",
            Mensaje = primerMensaje ?? "La peticion tiene datos invalidos.",
            Datos = errores.Count == 0 ? null : errores,
        });
    };
});

// ---------------------------------------------------------------------------
// Compresion: se omiten los tipos ya comprimidos para no hacer el trabajo dos veces.
// ---------------------------------------------------------------------------
builder.Services.AddResponseCompression(opciones =>
{
    opciones.EnableForHttps = true;
    opciones.MimeTypes = new[]
    {
        "application/json",
        "text/json",
        "text/plain",
        "text/csv",
        "application/javascript",
        "text/javascript",
    };
});

// ---------------------------------------------------------------------------
// Health checks. Uno por dependencia: un health check agregado que responde
// bien con la base caida no sirve para saber que ha pasado.
// ---------------------------------------------------------------------------
// Un check por dependencia, con etiqueta. Un health check agregado que
// responde bien con la base caida no sirve para saber que ha pasado.
builder.Services.AddHealthChecks()
    .AddCheck("vivo", () => HealthCheckResult.Healthy("El proceso responde."))
    .AddDbContextCheck<SolicitudDbContext>(
        name: "base-datos",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "db" });

// ---------------------------------------------------------------------------
// Kestrel. El limite de 30 minutos es para transferencias largas; el default
// de 100 segundos las cortaria a mitad sin avisar.
// ---------------------------------------------------------------------------
builder.WebHost.ConfigureKestrel(opciones =>
{
    opciones.Limits.MaxRequestBodySize = 100L * 1024 * 1024;
    opciones.AddServerHeader = false;
});

var app = builder.Build();

// ---------------------------------------------------------------------------
// Pipeline. El orden importa:
//   1. ExceptionMiddleware, para que capture TODO lo que viene despues.
//   2. HeaderMiddleware, antes de nada que toque la base: decide el enrutado.
//   3. Swagger, comprimir, CORS, rutas.
// ---------------------------------------------------------------------------
app.UseMiddleware<ExceptionMiddleware>();
app.UseMiddleware<HeaderMiddleware>();

app.UseResponseCompression();
app.UseCors("PoliticaPorDefecto");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(opciones =>
    {
        opciones.SwaggerEndpoint("/swagger/v1/swagger.json", "API de Transacciones v1");
        opciones.DocumentTitle = "API de Transacciones";
    });
}
else
{
    // Fuera de desarrollo la UI se puede dejar accesible pero sin UI interactiva.
    app.UseSwagger();
}

// El health check de la base va con etiqueta para poder exponerlo aparte del
// liveness: un readiness con la base caida debe ser un 503.
app.MapHealthChecks("/api/healthcheck", new()
{
    Predicate = registro => registro.Tags.Contains("db"),
    ResponseWriter = EscribirHealthAsync,
});

app.MapHealthChecks("/api/healthcheck/ready");

app.MapControllers();

app.Run();
return;

// El response writer devuelve JSON en vez del texto plano por defecto, que es
// dificil de consumir desde un monitor externo.
static Task EscribirHealthAsync(HttpContext contexto, HealthReport informe)
{
    contexto.Response.ContentType = "application/json; charset=utf-8";

    var cuerpo = new
    {
        estado = informe.Status.ToString(),
        totalMs = informe.TotalDuration.TotalMilliseconds,
        comprobaciones = informe.Entries.Select(e => new
        {
            nombre = e.Key,
            estado = e.Value.Status.ToString(),
            descripcion = e.Value.Description,
            ms = e.Value.Duration.TotalMilliseconds,
        }),
    };

    return contexto.Response.WriteAsync(JsonSerializer.Serialize(
        cuerpo,
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

/// <summary>
/// Punto de entrada explicito, necesario para que <c>WebApplicationFactory</c>
/// pueda localizar <c>Program</c> en las pruebas de integracion.
/// </summary>
public partial class Program;