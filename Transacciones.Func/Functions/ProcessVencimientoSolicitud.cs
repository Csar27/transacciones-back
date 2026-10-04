using Dapper;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Transacciones.CrossCutting.Constant;

namespace Transacciones.Func.Functions;

/// <summary>
/// Marca como vencidas las solicitudes cuya vigencia se cumplio.
/// <para>
/// Se ejecuta por reloj, una vez por hora. El trabajo real es un
/// <c>UPDATE</c> con filtro, asi que va por Dapper y no por EF: cargar miles de
/// filas en el change tracker para reescribir las mismas columnas es trabajo
/// que se tira.
/// </para>
/// <para>
/// La conexion se abre aqui y no por inyeccion. Es deliberado: un Function App
/// se despliega por separado y lleva su configuracion en sus Application
/// Settings. Arrastrar el <c>DbContext</c> del API hasta aqui solo para abrir
/// una conexion seria una dependencia que este proyecto no necesita.
/// </para>
/// <para>
/// Es idempotente: el filtro <c>Estado IN (0, 1)</c> hace que una segunda
/// ejecucion no cambie nada. Esa es la propiedad que lo hace seguro ante un
/// reintento o una instancia duplicada.
/// </para>
/// </summary>
public sealed class ProcessVencimientoSolicitud(ILogger<ProcessVencimientoSolicitud> logger)
{
    private const string Consulta = """
        UPDATE dbo.Solicitud
           SET Estado = 4,
               FechaModificacion = @FechaModificacion
         WHERE Estado IN (0, 1)
           AND FechaVencimiento < @FechaCorte;
        """;

    /// <param name="info">Datos de disparo del temporizador.</param>
    /// <param name="ct">Token de cancelacion de la invocacion.</param>
    [Function("ProcessVencimientoSolicitud")]
    public async Task RunAsync(
        [TimerTrigger("0 0 * * * *")] TimerInfo info,
        CancellationToken ct)
    {
        // El cron tiene 6 campos: segundo, minuto, hora, dia, mes y dia de la
        // semana. "0 0 * * * *" significa cada hora, en el minuto 0.
        var correlationId = $"vencimiento-{Guid.NewGuid():N}";

        using var traza = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["Funcion"] = nameof(ProcessVencimientoSolicitud),
        });

        var connectionString = Environment.GetEnvironmentVariable(
            $"{ConfigSections.Database}:ConnectionStringAFF");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            logger.LogError(
                "Falta la variable de entorno {Clave}:ConnectionStringAFF",
                ConfigSections.Database);

            throw new InvalidOperationException(
                $"Falta la configuracion {ConfigSections.Database}:ConnectionStringAFF.");
        }

        var ahora = DateTime.UtcNow;

        await using var conexion = new SqlConnection(connectionString);
        await conexion.OpenAsync(ct);

        var afectadas = await conexion.ExecuteAsync(Consulta, new
        {
            FechaCorte = ahora,
            FechaModificacion = ahora,
        });

        if (afectadas > 0)
        {
            logger.LogInformation("{Cantidad} solicitud(es) marcadas como vencidas", afectadas);
        }
    }
}