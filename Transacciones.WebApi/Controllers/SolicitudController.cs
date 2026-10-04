using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Transacciones.Core.Models;
using Transacciones.Core.Services;
using Transacciones.CrossCutting.Enums;
using Transacciones.CrossCutting.Model;

namespace Transacciones.WebApi.Controllers;

/// <summary>
/// Endpoints de Solicitud.
/// <para>
/// El controller es delgado a proposito: traduce HTTP a llamadas del servicio y
/// devuelve DTOs. Si aparece una regla de negocio aqui, es que se ha puesto en el
/// sitio equivocado.
/// </para>
/// </summary>
[ApiController]
[Route("api/solicitudes")]
[Produces("application/json")]
public sealed class SolicitudController(
    ISolicitudService servicio,
    IMapper mapper,
    ICurrentRequestContext contexto) : ControllerBase
{
    /// <summary>Lista paginada con filtros opcionales.</summary>
    /// <param name="pagina">Numero de pagina, desde 1.</param>
    /// <param name="tamanoPagina">Elementos por pagina, maximo 200.</param>
    /// <param name="codigo">Coincidencia parcial del codigo.</param>
    /// <param name="clienteId">Filtra por cliente.</param>
    /// <param name="estado">Filtra por estado.</param>
    /// <param name="desde">Fecha de solicitud minima.</param>
    /// <param name="hasta">Fecha de solicitud maxima.</param>
    /// <param name="ct">Token de cancelacion.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<SolicitudDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SolicitudDto>>> ListarAsync(
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 20,
        [FromQuery] string? codigo = null,
        [FromQuery] Guid? clienteId = null,
        [FromQuery] EnumEstadoSolicitud? estado = null,
        [FromQuery] DateTime? desde = null,
        [FromQuery] DateTime? hasta = null,
        CancellationToken ct = default)
    {
        var criteria = new SolicitudCriteria
        {
            Pagina = pagina,
            TamanoPagina = tamanoPagina,
            Codigo = codigo,
            ClienteId = clienteId,
            Estado = estado,
            Desde = desde,
            Hasta = hasta,
        };

        var resultado = await servicio.ListarAsync(criteria, ct).ConfigureAwait(false);

        // Se proyecta pagina a pagina, no el total: mapearlo todo para luego
        // tirar el 90% es trabajo tirado, y con paginas grandes se nota.
        var paginaDto = new PagedResult<SolicitudDto>(
            resultado.Items.Select(mapper.Map<SolicitudDto>).ToList(),
            resultado.TotalRegistros,
            resultado.Pagina,
            resultado.TamanoPagina);

        return Ok(ApiResponse<PagedResult<SolicitudDto>>.Ok(paginaDto));
    }

    /// <summary>Obtiene una solicitud por id.</summary>
    /// <param name="id">Identificador de la solicitud.</param>
    /// <param name="ct">Token de cancelacion.</param>
    /// <remarks>
    /// La ruta lleva <c>Name = "obtener-solicitud"</c> y no es decorativo: con
    /// attribute routing, un endpoint SOLO es direccionable por el generador de
    /// enlaces si su ruta tiene nombre. Sin el, el POST responde 500 con "No route
    /// matches the supplied values" justo al construir la cabecera Location.
    /// </remarks>
    [HttpGet("{id:guid}", Name = "obtener-solicitud")]
    [ProducesResponseType(typeof(SolicitudDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SolicitudDto>> ObtenerAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var solicitud = await servicio.ObtenerAsync(id, ct).ConfigureAwait(false);

        return Ok(ApiResponse<SolicitudDto>.Ok(mapper.Map<SolicitudDto>(solicitud)));
    }

    /// <summary>Crea una solicitud en estado Registrada.</summary>
    /// <param name="dto">Datos de la nueva solicitud.</param>
    /// <param name="ct">Token de cancelacion.</param>
    [HttpPost]
    [ProducesResponseType(typeof(SolicitudDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<SolicitudDto>> CrearAsync(
        [FromBody] CrearSolicitudDto dto,
        CancellationToken ct = default)
    {
        var solicitud = await servicio.CrearAsync(dto, ct).ConfigureAwait(false);
        var respuesta = mapper.Map<SolicitudDto>(solicitud);

        // CreatedAtRoute por NOMBRE de ruta, no CreatedAtAction por nombre de
        // accion. Con attribute routing el nombre de accion no siempre resuelve:
        // si no, el POST devuelve 500 al construir la cabecera Location, DESPUES
        // de haber guardado la solicitud. Un fallo invisible que deja datos
        // huerfanos y una respuesta de error al usuario.
        return CreatedAtRoute(
            routeName: "obtener-solicitud",
            routeValues: new { id = respuesta.Id },
            value: ApiResponse<SolicitudDto>.Ok(respuesta, "Solicitud creada."));
    }

    /// <summary>Aprueba la solicitud.</summary>
    /// <param name="id">Identificador de la solicitud.</param>
    /// <param name="ct">Token de cancelacion.</param>
    [HttpPut("{id:guid}/aprobar")]
    [ProducesResponseType(typeof(SolicitudDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<SolicitudDto>> AprobarAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var solicitud = await servicio.AprobarAsync(id, ct).ConfigureAwait(false);

        return Ok(ApiResponse<SolicitudDto>.Ok(mapper.Map<SolicitudDto>(solicitud)));
    }

    /// <summary>Rechaza la solicitud.</summary>
    /// <param name="id">Identificador de la solicitud.</param>
    /// <param name="motivo">Motivo del rechazo. Obligatorio.</param>
    /// <param name="ct">Token de cancelacion.</param>
    [HttpPut("{id:guid}/rechazar")]
    [ProducesResponseType(typeof(SolicitudDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SolicitudDto>> RechazarAsync(
        Guid id,
        [FromQuery] string motivo,
        CancellationToken ct = default)
    {
        var solicitud = await servicio.RechazarAsync(id, motivo, ct).ConfigureAwait(false);

        return Ok(ApiResponse<SolicitudDto>.Ok(mapper.Map<SolicitudDto>(solicitud)));
    }

    /// <summary>Anula la solicitud.</summary>
    /// <param name="id">Identificador de la solicitud.</param>
    /// <param name="ct">Token de cancelacion.</param>
    [HttpPut("{id:guid}/anular")]
    [ProducesResponseType(typeof(SolicitudDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SolicitudDto>> AnularAsync(
        Guid id,
        CancellationToken ct = default)
    {
        var solicitud = await servicio.AnularAsync(id, ct).ConfigureAwait(false);

        return Ok(ApiResponse<SolicitudDto>.Ok(mapper.Map<SolicitudDto>(solicitud)));
    }

    /// <summary>Id de correlacion de la peticion en curso. Util para soporte.</summary>
    [HttpGet("correlacion")]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status200OK)]
    public ActionResult<ApiResponse<object>> Correlacion() =>
        Ok(ApiResponse<object>.Ok(new { correlacionId = contexto.CorrelationId }));
}