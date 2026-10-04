using Transacciones.Core.Models;

namespace Transacciones.Core.Services;

/// <summary>
/// Salidas a otros servicios. La implementacion vive en Transacciones.External.
/// Declarar el contrato en Core es lo que permite que Services no dependa de
/// la infraestructura HTTP.
/// </summary>
public interface INotificacionService
{
    /// <summary>Envia un mensaje a un destinatario.</summary>
    Task EnviarAsync(string destinatario, string asunto, string cuerpo, CancellationToken ct = default);

    /// <summary>Aviso al cliente del cambio de estado de su solicitud.</summary>
    Task NotificarCambioEstadoAsync(Solicitud solicitud, CancellationToken ct = default);
}