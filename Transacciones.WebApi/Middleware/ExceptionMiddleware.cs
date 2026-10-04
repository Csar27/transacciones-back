using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Transacciones.CrossCutting.Exceptions;
using Transacciones.CrossCutting.Model;
using Transacciones.Core.Exceptions;

namespace Transacciones.WebApi.Middleware;

/// <summary>
/// Traduce excepciones a respuestas HTTP.
/// <para>
/// Es la unica pieza que decide el codigo de estado de un error. Sin ella, un
/// error de negocio sale como 500 con el detalle de la excepcion, y eso filtra
/// informacion interna.
/// </para>
/// <para>
/// Aqui el patron es explicito, no generico. El coste de tratar "cualquier
/// excepcion" como 500 es que se acaba devolviendo 500 para todo, y nadie
/// distingue un bug de un dato malo.
/// </para>
/// </summary>
public sealed class ExceptionMiddleware(
    RequestDelegate siguiente,
    ILogger<ExceptionMiddleware> logger,
    IHostEnvironment entorno)
{
    private static readonly JsonSerializerOptions OpcionesJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task InvokeAsync(HttpContext http)
    {
        try
        {
            await siguiente(http).ConfigureAwait(false);
        }
        catch (BusinessRuleException ex)
        {
            // Error previsto: no es un fallo del sistema, asi que no se registra
            // como error. Registrarl o inflaria las alertas por datos de usuario.
            logger.LogInformation(
                "Regla de negocio incumplida [{Codigo}]: {Mensaje}",
                ex.Codigo,
                ex.Message);

            await ResponderAsync(http, StatusCodes.Status400BadRequest, ex.Codigo, ex.Message)
                .ConfigureAwait(false);
        }
        catch (SolicitudNoEncontradaException ex)
        {
            logger.LogInformation("Recurso no encontrado: {Mensaje}", ex.Message);

            await ResponderAsync(http, StatusCodes.Status404NotFound, "GEN-404", ex.Message)
                .ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            await ResponderAsync(http, StatusCodes.Status401Unauthorized, "GEN-401", "No autenticado.")
                .ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Alguien modifico la fila entre nuestra lectura y la escritura.
            // 409: el cliente puede reintentar, a diferencia de un 400.
            logger.LogInformation("Conflicto de concurrencia al guardar");

            await ResponderAsync(
                http,
                StatusCodes.Status409Conflict,
                "GEN-409",
                "El registro fue modificado por otra peticion. Reintente.")
                .ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (EsClaveDuplicada(ex))
        {
            // Violacion de indice unico. 409 y no 500: la peticion no es invalida
            // y el usuario no va a arreglarla reintentando igual, pero el problema
            // es del servidor, no de sus datos. Un 500 aqui esconde un fallo real.
            logger.LogInformation("Clave duplicada al guardar: {Detalle}", ex.InnerException?.Message);

            await ResponderAsync(
                http,
                StatusCodes.Status409Conflict,
                "GEN-409",
                "Ya existe un registro con esos datos.")
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (http.RequestAborted.IsCancellationRequested)
        {
            // El cliente se fue. Nadie va a leer la respuesta, asi que basta con
            // registrarlo. Escribir una respuesta aqui suele lanzar otra excepcion.
            logger.LogInformation("Peticion cancelada por el cliente: {Ruta}", http.Request.Path);
        }
        catch (Exception ex)
        {
            // Esto si es un fallo nuestro: se registra con stack trace.
            logger.LogError(ex, "Error no controlado en {Metodo} {Ruta}", http.Request.Method, http.Request.Path);

            // El detalle NUNCA sale de desarrollo: ahi hay nombres de servidor,
            // rutas de archivos y a veces cadenas de conexion.
            //
            // En DESARROLLO si se dice el tipo y el mensaje, y solo eso. Un 500
            // generico obliga a adivinar, y adivinar entre un fallo de mapeo, uno
            // de SQL y uno de configuracion es tiempo perdido. El stack trace
            // sigue sin salir nunca: para eso esta el log.
            var mensaje = entorno?.IsDevelopment() == true
                ? $"{ex.GetType().Name}: {ex.Message}"
                : "Ocurrio un error al procesar la solicitud.";

            await ResponderAsync(
                http,
                StatusCodes.Status500InternalServerError,
                "GEN-500",
                mensaje)
                .ConfigureAwait(false);
        }
    }

    private static async Task ResponderAsync(
        HttpContext http,
        int statusCode,
        string codigo,
        string mensaje)
    {
        if (http.Response.HasStarted)
        {
            // Ya se escribieron bytes de la respuesta: a estas alturas no se
            // puede cambiar el codigo. Se avisa y no se intenta nada mas.
            return;
        }

        http.Response.Clear();
        http.Response.StatusCode = statusCode;
        http.Response.ContentType = "application/json; charset=utf-8";

        var contexto = http.RequestServices.GetService(typeof(ICurrentRequestContext))
            as ICurrentRequestContext;

        var respuesta = new ApiResponse<object>
        {
            Exito = false,
            Codigo = codigo,
            Mensaje = mensaje,
            CorrelationId = contexto?.CorrelationId
                ?? http.Response.Headers[HeaderNamesConst].FirstOrDefault(),
        };

        await http.Response
            .WriteAsync(JsonSerializer.Serialize(respuesta, OpcionesJson))
            .ConfigureAwait(false);
    }

    private const string HeaderNamesConst = "X_correlationId";

    /// <summary>
    /// Detecta violacion de indice unico o de clave primaria.
    /// <para>
    /// 2601 = violacion de indice unico. 2627 = violacion de clave primaria o
    /// unica. Unico: son las dos el mismo problema para el llamante.
    /// </para>
    /// </summary>
    private static bool EsClaveDuplicada(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}