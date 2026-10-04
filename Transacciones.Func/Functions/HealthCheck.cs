using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Transacciones.Func.Functions;

/// <summary>
/// Health check del Function App.
/// <para>
/// El balanceador de Azure sondea este endpoint para decidir si reparte
/// ejecuciones. Si responde 200 cuando no deberia, manda trabajo a un proceso
/// caido; si responde mal cuando funciona, se detiene solo.
/// </para>
/// <para>
/// Devuelve JSON y no texto plano porque lo consume un monitor externo, que lo
/// parsea mucho mas facil.
/// </para>
/// </summary>
public sealed class HealthCheck(ILogger<HealthCheck> logger)
{
    /// <param name="req">Contexto de la peticion HTTP.</param>
    [Function("HealthCheck")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] HttpRequestData req)
    {
        var hayConfiguracion = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("ABCMultiSettings:ConnectionStringAFF"));

        if (!hayConfiguracion)
        {
            logger.LogWarning("El health check se reporta como degradado: falta configuracion");
        }

        var respuesta = new
        {
            estado = hayConfiguracion ? "Healthy" : "Degraded",
            version = typeof(HealthCheck).Assembly.GetName().Version?.ToString(),
            comprobaciones = new
            {
                configuracion = hayConfiguracion ? "OK" : "Falta ConnectionStringAFF",
            },
        };

        var http = req.CreateResponse(
            hayConfiguracion ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable);

        http.Headers.Add("Content-Type", "application/json; charset=utf-8");

        // WriteStringAsync devuelve Task: hay que esperarlo antes de devolver la
    // respuesta, o el cuerpo se escribe a medias.
    await http.WriteStringAsync(JsonSerializer.Serialize(respuesta, new JsonSerializerOptions
    {
        WriteIndented = true,
    }));

        return http;
    }
}