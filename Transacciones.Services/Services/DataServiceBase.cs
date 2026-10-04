using Microsoft.Extensions.Logging;

namespace Transacciones.Services.Services;

/// <summary>
/// Base de los servicios de negocio. Concentra lo que todos repiten:
/// el logger y un metodo para validar reglas.
/// <para>
/// Existe para que cada servicio no reescriba el mismo constructor de cinco
/// parametros. Si un servicio nuevo no necesita mas, hereda de aqui y ya.
/// </para>
/// </summary>
public abstract class DataServiceBase(ILogger logger)
{
    protected ILogger Logger { get; } = logger;

    /// <summary>Nombre del servicio, para las trazas.</summary>
    protected virtual string Origen => GetType().Name;

    /// <summary>
    /// Atajo para abrir un ambito de log con la correlacion de la peticion.
    /// Devuelve un disposable: hay que envolverlo en <c>using</c>. Si el provider
    /// no admite ambitos, devuelve un sustituto inerte en vez de null.
    /// </summary>
    protected IDisposable LogScope(string? correlationId) =>
        Logger.BeginScope(new Dictionary<string, object>
        {
            ["Servicio"] = Origen,
            ["CorrelationId"] = correlationId ?? string.Empty,
        }) ?? NoopScope.Instance;

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
            // No hay nada que liberar.
        }
    }
}