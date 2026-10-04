using Microsoft.Extensions.Logging;

namespace Transacciones.CrossCutting.Logging;

/// <summary>
/// Correlacion en los logs. Sin esto, una peticion que atraviesa servicios distintos
/// deja un rastro imposible de reconstruir: cada linea dice algo distinto.
/// </summary>
public static class CorrelationLog
{
    public const string CorrelationIdKey = "CorrelationId";
    public const string OrigenKey = "Origen";

    /// <summary>
    /// Abre un ambito de log con la correlacion. Devuelve un disposable que lo cierra:
    /// hay que envolverlo en <c>using</c> o el ambito se filtra al siguiente ambito.
    /// </summary>
    public static IDisposable Entrar(this ILogger logger, string? correlationId, string origen)
    {
        ArgumentNullException.ThrowIfNull(logger);

        return logger.BeginScope(new Dictionary<string, object>
        {
            [CorrelationIdKey] = correlationId ?? string.Empty,
            [OrigenKey] = origen,
        }) ?? NoopScope.Instance;
    }

    /// <summary>
    /// Sustituto cuando el provider de logs no admite ambitos.
    /// Evita propagar el null al llamante y romper el patron using.
    /// </summary>
    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();

        public void Dispose()
        {
            // No hay nada que liberar.
        }
    }
}