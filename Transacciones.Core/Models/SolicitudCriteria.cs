namespace Transacciones.Core.Models;

/// <summary>
/// Filtro de busqueda. El repositorio lo recibe tal cual: la logica de paginacion
/// y filtrado vive en Data, no en el controller.
/// </summary>
public sealed record SolicitudCriteria
{
    /// <summary>Numero de pagina, empezando en 1.</summary>
    public int Pagina { get; init; } = 1;

    /// <summary>Elementos por pagina. El repositorio impone un tope maximo.</summary>
    public int TamanoPagina { get; init; } = 20;

    /// <summary>Codigo exacto o coincidencia parcial.</summary>
    public string? Codigo { get; init; }

    public Guid? ClienteId { get; init; }

    public CrossCutting.Enums.EnumEstadoSolicitud? Estado { get; init; }

    public DateTime? Desde { get; init; }

    public DateTime? Hasta { get; init; }

    /// <summary>Normaliza valores fuera de rango en lugar de fallar.</summary>
    public SolicitudCriteria Sanitizado() => this with
    {
        Pagina = Pagina < 1 ? 1 : Pagina,
        TamanoPagina = TamanoPagina switch
        {
            < 1 => 20,
            > 200 => 200,
            _ => TamanoPagina,
        },
    };
}