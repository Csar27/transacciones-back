namespace Transacciones.CrossCutting.Model;

/// <summary>
/// Configuracion de los clientes HTTP salientes.
/// <para>
/// Los reintentos van en el cliente y no en cada llamada: con Polly aqui, un
/// 503 del servicio externo se reintenta solo y con espera creciente.
/// </para>
/// </summary>
public sealed class ExternalApiSettings
{
    public const string NombreSeccion = "ExternalApis";

    /// <summary>URL base del servicio de notificaciones.</summary>
    public string NotificacionesUrl { get; set; } = string.Empty;

    /// <summary>URL base del catalogo de clientes.</summary>
    public string CatalogoUrl { get; set; } = string.Empty;

    /// <summary>Token de ejemplo. En produccion, renovacion automatica.</summary>
    public string? Token { get; set; }

    /// <summary>Numero de reintentos por defecto.</summary>
    public int Reintentos { get; set; } = 3;

    /// <summary>Espera inicial entre reintentos.</summary>
    public int EsperaInicialSegundos { get; set; } = 2;
}