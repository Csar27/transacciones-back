using Dapper;
using Quartz;

namespace Transacciones.WebApi.Jobs;

/// <summary>
/// Job que marca como vencidas las solicitudes cuya vigencia se cumplio.
/// <para>
/// Vive en su propia carpeta, aunque Quartz lo registra en
/// <c>Schedular</c>. La clase real del trabajo queda separada de la
/// configuracion del planificador: se puede probar sin levantar Quartz.
/// </para>
/// <para>
/// Es idempotente: el <c>UPDATE</c> solo toca estados no terminales, asi que
/// correrlo dos veces no cambia nada. Esa es la propiedad que lo hace seguro
/// ante un reintento o una instancia duplicada.
/// </para>
/// </summary>
public sealed class SolicitudVencidaJob(
    Data.Repositories.ISqlConnectionFactory conexiones,
    ILogger<SolicitudVencidaJob> logger) : IJob
{
    private const string Consulta = """
        UPDATE dbo.Solicitud
           SET Estado = 4,
               FechaModificacion = @FechaModificacion
         WHERE Estado IN (0, 1)
           AND FechaVencimiento < @FechaCorte;
        """;

    public async Task Execute(IJobExecutionContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        // El job corre fuera de una peticion: no hay token de cancelacion que
        // propagar y no se corta a mitad. Un job que se cancela a medias deja
        // el paso de vencimiento a medias.
        CancellationToken ct = CancellationToken.None;

        using var traza = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = $"quartz-{Guid.NewGuid():N}",
            ["Job"] = nameof(SolicitudVencidaJob),
            ["FireInstanceId"] = contexto.FireInstanceId,
        });

        try
        {
            var ahora = DateTime.UtcNow;

            await using var conexion = conexiones.Crear();

            var afectadas = await conexion.ExecuteAsync(Consulta, new
            {
                FechaCorte = ahora,
                FechaModificacion = ahora,
            }).ConfigureAwait(false);

            if (afectadas > 0)
            {
                logger.LogInformation(
                    "{Cantidad} solicitud(es) marcadas como vencidas",
                    afectadas);
            }
        }
        catch (Exception ex)
        {
            // Se relanza para que Quartz lo registre y reintente. Tragarse la
            // excepcion haria que el job pareciera correcto sin haber hecho nada.
            logger.LogError(ex, "Fallo el paso de vencimiento de solicitudes");
            throw;
        }
    }
}