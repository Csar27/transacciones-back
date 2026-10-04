using Transacciones.CrossCutting.Constant;
using Transacciones.CrossCutting.Model;

namespace Transacciones.WebApi.Middleware;

/// <summary>
/// Lee las cabeceras que condicionan el comportamiento de la peticion y las
/// deja en el contexto de request.
/// <para>
/// El caso importante es <c>X_isMultiRisk</c>: decide contra que base de datos
/// se atiende la peticion. Se resuelve aqui, al principio del pipeline, y no
/// dentro de los repositorios, para que la decision se vea en un solo sitio.
/// </para>
/// <para>
/// Si esta logica se repitiera por endpoint, tarde o temprano un controller
/// se olvidaria de honourarlo y escribiria contra la base equivocada.
/// </para>
/// </summary>
public sealed class HeaderMiddleware(RequestDelegate siguiente)
{
    public async Task InvokeAsync(HttpContext http, ICurrentRequestContext contexto)
    {
        // True solo con un valor inequivoco. "false", "1" o basura van a la base
        // normal: ante la duda, la ruta por defecto.
        var multiRisk = http.Request.Headers[HeaderNames.MultiRisk].FirstOrDefault();

        if (IsVerdadero(multiRisk))
        {
            contexto.IsMultiRisk = true;
        }

        // Si no viene, se genera. Sin esto, el log de un error suelto no se
        // puede correlacionar con nada.
        var correlacion = http.Request.Headers[HeaderNames.CorrelationId].FirstOrDefault();

        if (string.IsNullOrWhiteSpace(correlacion))
        {
            correlacion = Guid.NewGuid().ToString("N");
        }

        contexto.CorrelationId = correlacion;

        var usuario = http.Request.Headers[HeaderNames.UserId].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(usuario))
        {
            contexto.UserId = usuario;
        }

        // Se devuelve la cabecera en la respuesta para que el front pueda
        // citarla al reportar una incidencia.
        http.Response.Headers[HeaderNames.CorrelationId] = correlacion;

        await siguiente(http).ConfigureAwait(false);
    }

    /// <summary>
    /// Acepta los tres formatos que el front usa para un booleano.
    /// Cualquier otra cosa se considera falso.
    /// </summary>
    private static bool IsVerdadero(string? valor) =>
        bool.TryParse(valor, out var parsed)
            ? parsed
            : string.Equals(valor, "1", StringComparison.Ordinal);
}