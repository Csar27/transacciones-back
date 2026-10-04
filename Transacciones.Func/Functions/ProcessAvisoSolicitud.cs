using System.Text.Json;
using Dapper;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Transacciones.Func.Functions;

/// <summary>
/// Recibe avisos de estado desde la cola y los registra.
/// <para>
/// Es el equivalente de un consumidor de eventos. La diferencia con el job de
/// vencimiento es que este llega con datos: la cola ya trae el mensaje, no hay
/// que ir a buscarlo.
/// </para>
/// <para>
/// Muestra el patron de idempotencia por clave natural: se comprueba si el
/// <see cref="SolicitudVencimientoMensaje.EventoId"/> ya se proceso antes de
/// hacer nada. La cola entrega al menos una vez, asi que el mismo mensaje puede
/// llegar dos veces.
/// </para>
/// </summary>
public sealed class ProcessAvisoSolicitud(ILogger<ProcessAvisoSolicitud> logger)
{
    private const string Consulta = """
        IF EXISTS (SELECT 1 FROM dbo.EventoProcesado WHERE EventoId = @EventoId)
            RETURN;

        INSERT INTO dbo.EventoProcesado (EventoId, Tipo, RecibidoEn)
        VALUES (@EventoId, @Tipo, @RecibidoEn);
        """;

    /// <param name="mensaje">Mensaje de la cola, en JSON.</param>
    /// <param name="ct">Token de cancelacion de la invocacion.</param>
    /// <exception cref="InvalidOperationException">Si el mensaje no es valido.</exception>
    [Function("ProcessAvisoSolicitud")]
    public async Task RunAsync(
        [ServiceBusTrigger("avisos-solicitud")] string mensaje,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(mensaje))
        {
            logger.LogWarning("Llego un mensaje vacio a la cola; se descarta");
            return;
        }

        SolicitudVencimientoMensaje? aviso;

        try
        {
            aviso = JsonSerializer.Deserialize<SolicitudVencimientoMensaje>(mensaje);
        }
        catch (JsonException ex)
        {
            // Un mensaje ilegible no se va a reintentar: reintentarlo dara el
            // mismo error las tres veces y solo cargara la cola.
            logger.LogError(ex, "Mensaje no interpretable en la cola; se descarta");
            throw new InvalidOperationException("Mensaje con formato invalido.", ex);
        }

        if (aviso is null || aviso.EventoId == Guid.Empty)
        {
            logger.LogWarning("El mensaje no trae EventoId; se descarta");
            return;
        }

        var correlationId = aviso.CorrelacionId ?? $"cola-{Guid.NewGuid():N}";

        using var traza = logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId,
            ["EventoId"] = aviso.EventoId,
            ["Funcion"] = nameof(ProcessAvisoSolicitud),
        });

        var connectionString = Environment.GetEnvironmentVariable(
            "ABCMultiSettings:ConnectionStringAFF");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Falta la configuracion ABCMultiSettings:ConnectionStringAFF.");
        }

        await using var conexion = new SqlConnection(connectionString);
        await conexion.OpenAsync(ct);

        var insertados = await conexion.ExecuteAsync(Consulta, new
        {
            aviso.EventoId,
            aviso.Tipo,
            RecibidoEn = DateTime.UtcNow,
        });

        if (insertados == 0)
        {
            // Ya estaba procesado. Es el caso normal, no un fallo: la cola
            // reintenta hasta tener exito y por eso vuelve a entregar.
            logger.LogInformation("Evento {EventoId} ya procesado; se ignora", aviso.EventoId);
            return;
        }

        logger.LogInformation(
            "Aviso {Tipo} procesado para la solicitud {SolicitudId} (estado {Estado})",
            aviso.Tipo,
            aviso.SolicitudId,
            aviso.Estado);
    }
}

/// <summary>
/// Aviso de cambio de estado que llega por la cola.
/// <para>
/// Lleva su propio <c>EventoId</c>: es la clave de deduplicacion. Sin ella, un
/// reintento de la cola procesaria el mismo aviso dos veces.
/// </para>
/// </summary>
public sealed record SolicitudVencimientoMensaje
{
    public Guid EventoId { get; init; }

    public Guid SolicitudId { get; init; }

    public string? Tipo { get; init; }

    public int Estado { get; init; }

    public string? CorrelacionId { get; init; }

    public DateTimeOffset OcurredAt { get; init; }
}