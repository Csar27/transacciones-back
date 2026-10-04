namespace Transacciones.CrossCutting.Constant;

/// <summary>
/// Cabeceras HTTP que el front envia y que condicionan el comportamiento del API.
/// Nota: el prefijo <c>X_</c> es intencional y es como lo envia el front.
/// </summary>
public static class HeaderNames
{
    /// <summary>
    /// Cuando vale <c>true</c>, la peticion se enruta a la base multi-risk (SME).
    /// Lo inyecta <c>HeaderMiddleware</c>.
    /// </summary>
    public const string MultiRisk = "X_isMultiRisk";

    /// <summary>Identificador de correlacion para reconstruir una peticion completa en los logs.</summary>
    public const string CorrelationId = "X_correlationId";

    /// <summary>Usuario autenticado, propagado por el gateway.</summary>
    public const string UserId = "X_userId";
}