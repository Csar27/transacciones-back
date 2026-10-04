using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Transacciones.CrossCutting.Constant;
using Transacciones.CrossCutting.Model;

namespace Transacciones.Data.Repositories;

/// <summary>
/// Crea conexiones resueltas en el momento de usarlas.
/// <para>
/// Existe por el enrutado multi-base: la conexion correcta depende de la
/// peticion en curso (<c>X_isMultiRisk</c>), que no se conoce al construir el
/// contenedor. Resolver aqui significa que la decision se toma por peticion.
/// </para>
/// </summary>
public interface ISqlConnectionFactory
{
    SqlConnection Crear();
}

/// <summary>
/// Connection strings del servicio. Las dos conviven: la peticion decide cual se usa.
/// </summary>
public sealed class DatabaseSettings
{
    /// <summary>Base de datos normal (AFF).</summary>
    public string ConnectionStringAff { get; set; } = string.Empty;

    /// <summary>Base de datos multi-risk (SME).</summary>
    public string ConnectionStringSme { get; set; } = string.Empty;
}

/// <inheritdoc />
/// <remarks>
/// Si no hay peticion en curso (un job en segundo plano, por ejemplo) va a la
/// base normal: un job no pertenece a ninguna peticion concreta.
/// </remarks>
public sealed class SqlConnectionFactory(
    IOptionsMonitor<DatabaseSettings> opciones,
    ICurrentRequestContext contexto) : ISqlConnectionFactory
{
    public SqlConnection Crear()
    {
        var settings = opciones.CurrentValue;
        var escenario = contexto.IsMultiRisk ? "SME" : "AFF";

        var connectionString = contexto.IsMultiRisk
            ? settings.ConnectionStringSme
            : settings.ConnectionStringAff;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Falta la connection string del escenario '{escenario}' " +
                $"en la seccion '{ConfigSections.Database}'.");
        }

        return new SqlConnection(connectionString);
    }
}