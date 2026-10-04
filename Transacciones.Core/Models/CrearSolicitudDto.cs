using System.ComponentModel.DataAnnotations;

namespace Transacciones.Core.Models;

/// <summary>
/// Entrada para crear una solicitud. Es lo que llega del front, no lo que se
/// guarda: por eso no lleva Id, ni Estado, ni fechas de auditoria.
/// </summary>
public sealed class CrearSolicitudDto
{
    [Required(ErrorMessage = "El cliente es obligatorio.")]
    public Guid ClienteId { get; init; }

    [Range(0.01, 9_999_999.99, ErrorMessage = "El monto debe estar entre 0.01 y 9999999.99.")]
    public decimal Monto { get; init; }

    [StringLength(3, MinimumLength = 3, ErrorMessage = "La moneda debe tener 3 caracteres.")]
    public string Moneda { get; init; } = "PEN";

    [Range(1, 365, ErrorMessage = "La vigencia debe estar entre 1 y 365 dias.")]
    public int VigenciaDias { get; init; } = 30;

    [StringLength(500)]
    public string? Observacion { get; init; }
}