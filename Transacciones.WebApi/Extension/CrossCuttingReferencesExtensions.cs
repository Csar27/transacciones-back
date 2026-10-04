using Transacciones.CrossCutting.Model;

namespace Transacciones.WebApi.Extension;

/// <summary>
/// Lo compartido por todas las capas: contexto de peticion y configuracion
/// comun. Va en su propia extension para que quede claro que no pertenece a
/// ninguna capa concreta.
/// </summary>
public static class CrossCuttingReferencesExtensions
{
    public static IServiceCollection AddCrossCuttingReferences(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        // Scoped, no singleton: guarda estado de la peticion. Un singleton
        // seria compartido por todas las peticiones simultaneas y el
        // enrutado X_isMultiRisk se mezclaria entre usuarios.
        servicios.AddScoped<ICurrentRequestContext, CurrentRequestContext>();

        return servicios;
    }
}