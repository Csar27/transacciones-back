namespace Transacciones.CrossCutting.Models;

/// <summary>
/// Campos de auditoria comunes a las entidades. Se mapea en Core.
/// No es una entidad por si misma: se hereda o se compone.
/// </summary>
public abstract class AuditableEntity
{
    public DateTime FechaCreacion { get; set; }

    public string CreadoPor { get; set; } = string.Empty;

    public DateTime? FechaModificacion { get; set; }

    public string? ModificadoPor { get; set; }
}