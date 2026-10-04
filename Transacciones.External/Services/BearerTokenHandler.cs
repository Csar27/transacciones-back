using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Transacciones.External.Services;

/// <summary>
/// Anade el token de portador a las llamadas salientes.
/// <para>
/// El token se toma de <see cref="ITokenProvider"/> en cada llamada, no se fija
/// al construir el cliente: los tokens expiran y uno cacheado en el
/// DelegatingHandler acabaria con peticiones 401.
/// </para>
/// </summary>
public sealed class BearerTokenHandler(ITokenProvider tokens, ILogger<BearerTokenHandler> logger)
    : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await tokens.ObtenerTokenAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(token))
        {
            // No abortamos: hay servicios externos que aceptan llamadas sin token.
            // Dejarla pasar y que el servicio responda 401 es mas util que un 500 nuestro.
            logger.LogDebug(
                "No hay token para {Url}; la llamada sale sin cabecera Authorization",
                request.RequestUri);
        }
        else
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// Fuente del token de los servicios externos.
/// <para>
/// Es una abstraccion y no una implementacion concreta a proposito: la forma de
/// obtenerlo (Identity Server, gateway, token estatico) es infraestructura, y aqui
/// solo hace falta el contrato.
/// </para>
/// </summary>
public interface ITokenProvider
{
    /// <summary>Devuelve el token vigente, o null si no hay.</summary>
    Task<string?> ObtenerTokenAsync(CancellationToken ct = default);
}

/// <summary>
/// Implementacion de ejemplo: lee el token de un secreto.
/// <para>
/// Sirve para desarrollo. En produccion lo habitual es un cliente de OAuth con
/// renovacion automatica, o reenviar el token del llamante (delegacion).
/// </para>
/// </summary>
public sealed class ConfigurationTokenProvider(IConfiguration configuracion) : ITokenProvider
{
    private const string Clave = "ExternalApis:Token";

    public Task<string?> ObtenerTokenAsync(CancellationToken ct = default) =>
        Task.FromResult(configuracion[Clave]);
}