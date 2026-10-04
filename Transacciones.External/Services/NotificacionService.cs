using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Transacciones.Core.Models;
using Transacciones.Core.Services;
using Transacciones.CrossCutting.Enums;

namespace Transacciones.External.Services;

/// <summary>
/// Cliente del servicio de notificaciones.
/// <para>
/// Implementa el contrato de Core, no al reves. Services no sabe que esto es
/// HTTP: si manana el destino es una cola o un correo, no cambia nada.
/// </para>
/// </summary>
public sealed class NotificacionService(
    HttpClient httpClient,
    ILogger<NotificacionService> logger)
    : ApiServiceBase(httpClient, logger, nameof(NotificacionService)), INotificacionService
{
    private const string RutaEnvio = "api/v1/notificaciones";

    public async Task EnviarAsync(
        string destinatario,
        string asunto,
        string cuerpo,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinatario);

        var payload = new
        {
            destinatario,
            asunto,
            cuerpo,
            // La fecha la pone el receptor; mandarla evita que un reloj
            // desincronizado invierta el orden de las marcas de tiempo.
            generadoEn = DateTimeOffset.UtcNow,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, RutaEnvio)
        {
            Content = JsonContent.Create(payload),
        };

        using var respuesta = await HttpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        if (!respuesta.IsSuccessStatusCode)
        {
            logger.LogError(
                "El servicio de notificaciones respondio {Codigo} al enviar a {Destinatario}",
                (int)respuesta.StatusCode,
                destinatario);
        }
    }

    public Task NotificarCambioEstadoAsync(Solicitud solicitud, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);

        var asunto = $"Solicitud {solicitud.Codigo}: {DescribirEstado(solicitud.Estado)}";

        var cuerpo =
            $"""
             Hola,

             Tu solicitud {solicitud.Codigo} por {solicitud.Monto:N2} {solicitud.Moneda}
             ha cambiado a: {DescribirEstado(solicitud.Estado)}.

             {(solicitud.MotivoRechazo is null ? string.Empty : $"Motivo: {solicitud.MotivoRechazo}")}

             Cliente: {solicitud.ClienteId}
             """;

        // Sin destinatario no hay nada que enviar. Se registra y se sale sin
        // fallar: la peticion principal ya esta guardada.
        if (solicitud.ClienteId == Guid.Empty)
        {
            logger.LogWarning("La solicitud {Codigo} no tiene cliente; no se notifica", solicitud.Codigo);
            return Task.CompletedTask;
        }

        return EnviarAsync(solicitud.ClienteId.ToString(), asunto, cuerpo, ct);
    }

    private static string DescribirEstado(EnumEstadoSolicitud estado) => estado switch
    {
        EnumEstadoSolicitud.Borrador => "borrador",
        EnumEstadoSolicitud.Registrada => "registrada",
        EnumEstadoSolicitud.Aprobada => "aprobada",
        EnumEstadoSolicitud.Rechazada => "rechazada",
        EnumEstadoSolicitud.Vencida => "vencida",
        EnumEstadoSolicitud.Anulada => "anulada",
        _ => estado.ToString(),
    };
}