using Transacciones.Core.Services;
using Transacciones.Services.Services;

namespace Transacciones.WebApi.Extension;

/// <summary>
/// Registro de la capa de servicios (logica de negocio).
/// <para>
/// Cada servicio se registra contra su interfaz, nunca contra su clase concreta.
/// Asi los tests pueden sustituirlo por un mock sin tocar el contenedor.
/// </para>
/// </summary>
public static class ServicesExtensions
{
    public static IServiceCollection AddServices(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios.AddScoped<ISolicitudService, SolicitudService>();

        return servicios;
    }
}