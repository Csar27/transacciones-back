using Transacciones.CrossCutting.Enums;

namespace Transacciones.Core.Models;

/// <summary>
/// Forma de la solicitud hacia el front.
/// Se separa de la entidad a proposito: cambiar la entidad no debe romper al
/// front, y el front no debe ver campos internos.
/// </summary>
public sealed class SolicitudDto
{
    public Guid Id { get; init; }

    public string Codigo { get; init; } = string.Empty;

    public Guid ClienteId { get; init; }

    public decimal Monto { get; init; }

    public string Moneda { get; init; } = string.Empty;

    public EnumEstadoSolicitud Estado { get; init; }

    /// <summary>El estado en texto, para que el front no tenga que mapear el enum.</summary>
    public string EstadoDescripcion { get; init; } = string.Empty;

    public DateTime FechaSolicitud { get; init; }

    public DateTime FechaVencimiento { get; init; }

    public string? Observacion { get; init; }

    public DateTime? FechaAprobacion { get; init; }

    public string? MotivoRechazo { get; init; }

    public string CreadoPor { get; init; } = string.Empty;

    public DateTime FechaCreacion { get; init; }
}