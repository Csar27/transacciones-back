namespace Transacciones.Core.Models;

/// <summary>
/// Cliente que devuelve el catalogo. Vive en Core porque los DTOs de los
/// servicios externos tambien son contrato: si el mapeo cambio, el contrato
/// cambio, y eso debe verse en la compilacion.
/// </summary>
public sealed class ClienteDto
{
    public Guid Id { get; init; }

    public string Codigo { get; init; } = string.Empty;

    public string RazonSocial { get; init; } = string.Empty;

    public string? Correo { get; init; }

    public bool Activo { get; init; }
}