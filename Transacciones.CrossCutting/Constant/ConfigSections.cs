namespace Transacciones.CrossCutting.Constant;

/// <summary>
/// Nombres de las secciones de configuracion. Centralizarlos evita literales
/// repetidos por ahi, que es como aparecen los typos silenciosos.
/// </summary>
public static class ConfigSections
{
    /// <summary>
    /// Seccion con las connection strings del servicio. Contiene
    /// <c>ConnectionStringAFF</c> (base normal) y <c>ConnectionStringSME</c> (multi-risk).
    /// </summary>
    public const string Database = "ABCMultiSettings";

    /// <summary>Parametros de los clientes HTTP salientes.</summary>
    public const string ExternalApis = "ExternalApis";

    /// <summary>Clave de licencia de AutoMapper 15+.</summary>
    public const string AutoMapper = "AutoMapper";
}