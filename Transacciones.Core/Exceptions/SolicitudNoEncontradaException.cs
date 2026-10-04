namespace Transacciones.Core.Exceptions;

/// <summary>
/// La entidad existe en el contrato pero no en la base de datos.
/// Se traduce a HTTP 404. Es distinto de un error de validacion (400): aqui el
/// cliente pidio algo real, simplemente no esta.
/// </summary>
public sealed class SolicitudNoEncontradaException : Exception
{
    public Guid Id { get; }

    public SolicitudNoEncontradaException(Guid id)
        : base($"No existe una solicitud con el id {id}.")
    {
        Id = id;
    }

    public SolicitudNoEncontradaException(Guid id, string codigo)
        : base($"No existe una solicitud con el id {id} (codigo {codigo}).")
    {
        Id = id;
    }
}