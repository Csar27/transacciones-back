namespace Transacciones.CrossCutting.Model;

/// <summary>
/// Sobre de respuesta estandar de la API.
/// Evita que cada controller invente su propia forma de devolver errores.
/// </summary>
/// <typeparam name="T">Tipo del dato, o <see cref="object"/> en respuestas sin cuerpo.</typeparam>
public sealed record ApiResponse<T>
{
    /// <summary>True si la operacion concluded correctamente.</summary>
    public bool Exito { get; init; }

    /// <summary>Payload. Null en respuestas de error.</summary>
    public T? Datos { get; init; }

    /// <summary>Codigo de error de negocio. Null si <see cref="Exito"/> es true.</summary>
    public string? Codigo { get; init; }

    /// <summary>Mensaje legible del error o del resultado.</summary>
    public string? Mensaje { get; init; }

    /// <summary>Id de correlacion, para que el usuario pueda reportar el incidente.</summary>
    public string? CorrelationId { get; init; }

    public static ApiResponse<T> Ok(T datos, string? mensaje = null) =>
        new() { Exito = true, Datos = datos, Mensaje = mensaje };

    public static ApiResponse<T> Error(string codigo, string mensaje) =>
        new() { Exito = false, Codigo = codigo, Mensaje = mensaje };
}