using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Transacciones.Core.Models;

namespace Transacciones.External.Services;

/// <summary>
/// Cliente del catalogo de clientes (uno de los servicios del ecosistema).
/// <para>
/// Sirve de ejemplo de cliente de solo lectura: hereda de
/// <see cref="ApiServiceBase"/> y no anade nada, porque la base ya resuelve
/// URL, cabecera, traza y errores.
/// </para>
/// </summary>
public sealed class CatalogoService(
    HttpClient httpClient,
    ILogger<CatalogoService> logger)
    : ApiServiceBase(httpClient, logger, nameof(CatalogoService))
{
    public Task<ClienteDto?> ObtenerClienteAsync(Guid clienteId, CancellationToken ct = default) =>
        GetAsync<ClienteDto>($"api/v1/clientes/{clienteId}", ct);

    /// Si el servicio responde con una lista vacia o nula, se devuelve vacia: el
    /// llamante no debería tener que distinguir ambos casos.
    /// </summary>
    public async Task<IEnumerable<ClienteDto>> BuscarAsync(
        string termino,
        CancellationToken ct = default)
    {
        var resultado = await GetAsync<IEnumerable<ClienteDto>>(
            $"api/v1/clientes?search={Uri.EscapeDataString(termino)}",
            ct);

        return resultado ?? [];
    }
}