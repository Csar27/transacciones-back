namespace Transacciones.CrossCutting.Model;

/// <summary>
/// Contexto de la peticion en curso. Registrado como scoped: una instancia por request.
/// Lo consume el middleware de cabeceras y lo leen repositorios y servicios para
/// decidir a que base de datos enrutar.
/// <para>
/// Las propiedades tienen setter porque es el middleware quien las escribe. Un
/// consumidor de solo lectura (repositorios, servicios) debe usar esta interfaz;
/// quien la rellena usa <see cref="CurrentRequestContext"/>.
/// </para>
/// </summary>
public interface ICurrentRequestContext
{
    /// <summary>
    /// Indica si la peticion debe atenderse contra la base multi-risk (SME).
    /// Lo decide la cabecera <c>X_isMultiRisk</c>.
    /// </summary>
    bool IsMultiRisk { get; set; }

    /// <summary>Id de correlacion. Se genera si la cabecera no viene informada.</summary>
    string CorrelationId { get; set; }

    /// <summary>Usuario autenticado, o null si la peticion es anonima.</summary>
    string? UserId { get; set; }
}

/// <inheritdoc />
public sealed class CurrentRequestContext : ICurrentRequestContext
{
    public bool IsMultiRisk { get; set; }

    public string CorrelationId { get; set; } = string.Empty;

    public string? UserId { get; set; }
}