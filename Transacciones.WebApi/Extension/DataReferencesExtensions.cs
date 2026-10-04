using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Transacciones.Core.Repositories;
using Transacciones.CrossCutting.Constant;
using Transacciones.CrossCutting.Model;
using Transacciones.Data.Persistence;
using Transacciones.Data.Repositories;

namespace Transacciones.WebApi.Extension;

/// <summary>
/// Registro de la capa de datos.
/// <para>
/// Cada extension corresponde a una capa. Es lo que evita que
/// <c>Program.cs</c> crezca hasta ser ilegible y lo que hace visible de un
/// vistazo a quien registra que.
/// </para>
/// </summary>
public static class DataReferencesExtensions
{
    public static IServiceCollection AddDataReferences(
        this IServiceCollection servicios,
        IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        // Las dos connection strings se validan al arrancar. Si falta una, es
        // mejor que la aplicacion no levante que descubrirlo en el primer
        // request que necesite esa base.
        servicios.AddOptions<DatabaseSettings>()
            .Bind(configuracion.GetSection(ConfigSections.Database))
            .Validate(
                s => !string.IsNullOrWhiteSpace(s.ConnectionStringAff),
                "Falta ABCMultiSettings:ConnectionStringAff.")
            .Validate(
                s => !string.IsNullOrWhiteSpace(s.ConnectionStringSme),
                "Falta ABCMultiSettings:ConnectionStringSme.")
            .ValidateOnStart();

        // El contexto se registra CON DI por peticion para que la connection
        // string se resuelva en tiempo de peticion segun X_isMultiRisk.
        servicios.AddDbContext<SolicitudDbContext>((proveedor, opciones) =>
        {
            var settings = proveedor.GetRequiredService<IOptions<DatabaseSettings>>().Value;
            var contexto = proveedor.GetRequiredService<ICurrentRequestContext>();

            var connectionString = contexto.IsMultiRisk
                ? settings.ConnectionStringSme
                : settings.ConnectionStringAff;

            opciones.UseSqlServer(connectionString, sql =>
            {
                sql.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null);
                sql.CommandTimeout(60);
            });
        });

        servicios.AddScoped<ISqlConnectionFactory, SqlConnectionFactory>();
        servicios.AddScoped<SqlExecutor>();
        servicios.AddScoped<ISolicitudRepository, SolicitudRepository>();

        return servicios;
    }
}