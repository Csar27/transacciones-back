using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Transacciones.Core.Services;
using Transacciones.CrossCutting.Model;
using Transacciones.External.Services;

namespace Transacciones.WebApi.Extension;

/// <summary>
/// Registro de la capa de integraciones (clientes HTTP salientes).
/// <para>
/// Un cliente por servicio externo, con Polly montado en el pipeline.
/// </para>
/// <para>
/// Se usa <see cref="IHttpClientFactory"/> y no un <c>new HttpClient()</c>: un
/// cliente por peticion agota los sockets bajo carga.
/// </para>
/// </summary>
public static class ApiReferencesExtensions
{
    /// <summary>
    /// Registra los clientes HTTP salientes.
    /// </summary>
    public static IServiceCollection AddApiReferences(
        this IServiceCollection servicios,
        IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        var opciones = configuracion
            .GetSection(ExternalApiSettings.NombreSeccion)
            .Get<ExternalApiSettings>() ?? new ExternalApiSettings();

        servicios.AddOptions<ExternalApiSettings>()
            .Bind(configuracion.GetSection(ExternalApiSettings.NombreSeccion))
            .ValidateOnStart();

        // El handler de token va en el pipeline de TODOS los clientes, asi que se
        // registra como transitorio: HttpClientFactory lo instancia por cliente.
        servicios.AddTransient<BearerTokenHandler>();
        servicios.AddSingleton<ITokenProvider, ConfigurationTokenProvider>();

        RegistrarNotificaciones(servicios, opciones.NotificacionesUrl);
        RegistrarCatalogo(servicios, opciones.CatalogoUrl);

        return servicios;
    }

    /// <summary>
    /// Cliente de notificaciones.
    /// <para>
    /// OJO con la sobrecarga: <c>AddHttpClient&lt;TInterface, TImpl&gt;</c> es la
    /// que registra el SERVICIO contra la INTERFAZ. Con
    /// <c>AddHttpClient&lt;TImpl&gt;()</c> queda registrada solo la clase, y
    /// cualquier consumidor que pida <c>INotificacionService</c> falla al
    /// construir el contenedor con "Unable to resolve service for type".
    /// </para>
    /// </summary>
    private static void RegistrarNotificaciones(IServiceCollection servicios, string urlBase)
    {
        if (string.IsNullOrWhiteSpace(urlBase))
        {
            // Se omite en vez de fallar: permite levantar la API sin los externos
            // configurados. El error sale al usar el cliente, no al arrancar.
            return;
        }

        servicios.AddHttpClient<INotificacionService, NotificacionService>(cliente =>
            {
                cliente.BaseAddress = new Uri(urlBase);

                // 3 segundos, y no 30.
                //
                // El aviso es lo ULTIMO que hace la transicion, y la transicion ya
                // esta guardada. Con 30 segundos por intento y tres reintentos,
                // si el servicio de notificaciones no responde el usuario espera
                // mas de minuto y medio con el boton en "cargando" — y axios
                // abandona a los 20. El resultado es lo peor de los dos mundos:
                // el estado guardado y un error en pantalla.
                //
                // Un aviso que tarda mas de 3 segundos no es un aviso, es un
                // aviso perdido. Se pierde igual, pero el usuario no espera.
                cliente.Timeout = TimeSpan.FromSeconds(3);
                cliente.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddPolicyHandler(Resiliencia());
    }

    /// <summary>
    /// Cliente del catalogo. Se registra contra la clase concreta porque no tiene
    /// contrato en Core: es un consumidor opcional, no una dependencia de negocio.
    /// </summary>
    private static void RegistrarCatalogo(IServiceCollection servicios, string urlBase)
    {
        if (string.IsNullOrWhiteSpace(urlBase))
        {
            return;
        }

        servicios.AddHttpClient<CatalogoService>(cliente =>
            {
                cliente.BaseAddress = new Uri(urlBase);
                cliente.Timeout = TimeSpan.FromSeconds(15);
                cliente.DefaultRequestHeaders.Accept.ParseAdd("application/json");
            })
            .AddHttpMessageHandler<BearerTokenHandler>()
            .AddPolicyHandler(Resiliencia());
    }

    /// <summary>
    /// Reintentos con espera exponencial.
    /// <para>
    /// El jitter no es adorno: sin el, N clientes que fallan a la vez reintentan
    /// a la vez y vuelven a tumbar al servicio.
    /// </para>
    /// <para>
    /// Solo reintenta errores transitorios (5xx, timeout, red, 429). Un 400 es un
    /// error del llamante: reintentarlo dara el mismo 400 tres veces.
    /// </para>
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> Resiliencia() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(r => r.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                            or System.Net.HttpStatusCode.RequestTimeout)
            .WaitAndRetryAsync(
                // Esperas de sub-segundo, no de 2/4/8 segundos.
                //
                // El servicio de notificaciones es interno, en la misma red y en el
                // mismo entorno. Con esperas de segundos, si no esta levantado la
                // accion se queda 14 segundos esperando antes de responder: el
                // estado ya estaba guardado, pero el usuario ve una aplicacion
                // colgada. Sub-segundo mantiene el reintento —que sirve para un
                // pico de carga o un reinicio— sin que se note.
                new[]
                {
                    TimeSpan.FromMilliseconds(200),
                    TimeSpan.FromMilliseconds(400),
                    TimeSpan.FromMilliseconds(800),
                },
                onRetry: (respuesta, espera, _) =>
                {
                    // DelegateResult puede traer EXCEPCION o RESPUESTA, nunca
                    // las dos. Cuando no hubo respuesta —el servicio esta caido y
                    // el socket da refused— Result es null, y desreferenciarlo
                    // lanza NullReferenceException DENTRO de la politica de
                    // reintentos: se tapa el fallo real con otro que no dice
                    // nada de que paso.
                    //
                    // A la consola y no al logger: dentro del pipeline de Polly no
                    // hay un ILogger inyectado con facilidad, y una traza que no
                    // sale es peor que ninguna.
                    var causa = respuesta.Exception is not null
                        ? respuesta.Exception.GetType().Name
                        : $"HTTP {(int)respuesta.Result!.StatusCode}";

                    Console.Error.WriteLine(
                        $"Reintentando tras {causa}. Espera {espera.TotalSeconds:N0}s.");
                });
}