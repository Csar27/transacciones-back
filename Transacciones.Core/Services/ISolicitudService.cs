using Transacciones.Core.Models;
using Transacciones.CrossCutting.Model;

namespace Transacciones.Core.Services;

/// <summary>
/// Casos de uso de Solicitud. La implementacion vive en Transacciones.Services;
/// aqui solo se declara el contrato, para que las capas superiores dependan de
/// la abstraccion y no de la implementacion.
/// </summary>
public interface ISolicitudService
{
    /// <summary>Lista paginada y filtrada.</summary>
    Task<PagedResult<Solicitud>> ListarAsync(SolicitudCriteria criteria, CancellationToken ct = default);

    /// <summary>
    /// Obtiene una solicitud por id.
    /// </summary>
    /// <exception cref="Core.Exceptions.SolicitudNoEncontradaException">Si no existe.</exception>
    Task<Solicitud> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>Crea la solicitud en estado Borrador y la registra.</summary>
    Task<Solicitud> CrearAsync(CrearSolicitudDto dto, CancellationToken ct = default);

    /// <summary>Registra -> Aprobada.</summary>
    Task<Solicitud> AprobarAsync(Guid id, CancellationToken ct = default);

    /// <summary>Registrada -> Rechazada. El motivo es obligatorio.</summary>
    Task<Solicitud> RechazarAsync(Guid id, string motivo, CancellationToken ct = default);

    /// <summary>Anula la solicitud, sea cual sea su estado no terminal.</summary>
    Task<Solicitud> AnularAsync(Guid id, CancellationToken ct = default);
}