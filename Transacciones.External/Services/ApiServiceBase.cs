using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Transacciones.External.Services;

/// <summary>
/// Base de los clientes HTTP salientes.
/// <para>
/// Concentra lo comun a todos: URL base, cabecera de aceptacion, traducion de
/// errores HTTP a excepciones y traza de cada llamada. Un cliente concreto solo
/// aporta sus endpoints.
/// </para>
/// </summary>
public abstract class ApiServiceBase(
    HttpClient httpClient,
    ILogger logger,
    string nombreServicio) : IDisposable
{
    protected HttpClient HttpClient { get; } = httpClient;

    protected ILogger Logger { get; } = logger;

    /// <summary>Nombre para las trazas: saber que servicio salio es la mitad del diagnostico.</summary>
    protected string NombreServicio { get; } = nombreServicio;

    /// <summary>
    /// GET con deserializacion. Lanza <see cref="ExternalServiceException"/>
    /// si el servicio responde con error.
    /// </summary>
    protected async Task<T?> GetAsync<T>(string ruta, CancellationToken ct = default)
    {
        using var traza = Logger.BeginScope(new Dictionary<string, object>
        {
            ["ServicioExterno"] = NombreServicio,
            ["Ruta"] = ruta,
        });

        try
        {
            using var respuesta = await HttpClient
                .GetAsync(ruta, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);

            await EnsureSuccessAsync(respuesta, ct).ConfigureAwait(false);

            return await respuesta.Content
                .ReadFromJsonAsync<T>(cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelacion solicitada por el llamante: no es un fallo del servicio.
            throw;
        }
        catch (ExternalServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Fallo la llamada a {Servicio} en {Ruta}", NombreServicio, ruta);
            throw new ExternalServiceException(NombreServicio, ruta, ex);
        }
    }

    /// <summary>
    /// Traduce los errores HTTP a una excepcion con contexto.
    /// <para>
    /// Sin esto, un 404 del servicio remoto llega al front como un 500 opaco y
    /// nadie sabe que fallo ni donde.
    /// </para>
    /// </summary>
    private async Task EnsureSuccessAsync(HttpResponseMessage respuesta, CancellationToken ct)
    {
        if (respuesta.IsSuccessStatusCode)
        {
            return;
        }

        var cuerpo = await respuesta.Content
            .ReadAsStringAsync(ct)
            .ConfigureAwait(false);

        Logger.LogError(
            "{Servicio} respondio {Codigo} en {Url}: {Cuerpo}",
            NombreServicio,
            (int)respuesta.StatusCode,
            respuesta.RequestMessage?.RequestUri,
            cuerpo.Length > 500 ? cuerpo[..500] : cuerpo);

        throw new ExternalServiceException(
            NombreServicio,
            respuesta.RequestMessage?.RequestUri?.ToString() ?? "(url desconocida)",
            $"HTTP {(int)respuesta.StatusCode} ({respuesta.StatusCode}): {Truncar(cuerpo, 300)}");
    }

    private static string Truncar(string texto, int maximo) =>
        string.IsNullOrEmpty(texto) || texto.Length <= maximo ? texto : texto[..maximo] + "...";

    public void Dispose() => HttpClient.Dispose();
}

/// <summary>
/// Fallo hablando con un servicio externo. Distinta de un error de negocio: esta
/// no la puede arreglar el usuario reintentando la misma peticion.
/// </summary>
public sealed class ExternalServiceException : Exception
{
    public string Servicio { get; }

    public string? Url { get; }

    public ExternalServiceException(string servicio, string url, string detalle)
        : base($"El servicio '{servicio}' fallo en {url}: {detalle}")
    {
        Servicio = servicio;
        Url = url;
    }

    public ExternalServiceException(string servicio, string url, Exception inner)
        : base($"El servicio '{servicio}' fallo en {url}: {inner.Message}", inner)
    {
        Servicio = servicio;
        Url = url;
    }
}