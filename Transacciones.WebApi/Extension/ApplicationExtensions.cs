using System.Reflection;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Transacciones.CrossCutting.Constant;

namespace Transacciones.WebApi.Extension;

/// <summary>
/// Configuracion transversal: AutoMapper, CORS, JSON y Swagger.
/// </summary>
public static class ApplicationExtensions
{
    /// <summary>
    /// Registra AutoMapper con los perfiles de este ensamblado.
    /// <para>
    /// Se usa el registro por ensamblado, no <c>AddAutoMapper(Assembly)</c> a
    /// pelo, para poder inyectar la licencia desde configuracion (AutoMapper 15+).
    /// </para>
    /// </summary>
    public static IServiceCollection RegisterAutoMapper(
        this IServiceCollection servicios,
        IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        var licencia = configuracion[$"{ConfigSections.AutoMapper}:LicenseKey"] ?? string.Empty;

        servicios.AddAutoMapper(config =>
        {
            config.AddMaps(Assembly.GetExecutingAssembly());

            // AutoMapper 15+ exige registrar la licencia. La clave se lee de
            // configuracion para no dejarla escrita en el codigo. Vacia funciona
            // en desarrollo, pero hay que registrar la licencia antes de produccion.
            config.LicenseKey = licencia;
        });

        return servicios;
    }

    /// <summary>
    /// CORS. La lista de origenes viene de configuracion, no escrita aqui:
    /// un origen permitido en el codigo es un origen permitido para siempre.
    /// </summary>
    public static IServiceCollection AddCORS(this IServiceCollection servicios, IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        var origenes = configuracion.GetSection("Cors:Origenes").Get<string[]>() ?? [];

        servicios.AddCors(opciones =>
        {
            opciones.AddPolicy("PoliticaPorDefecto", politica =>
            {
                if (origenes.Length == 0)
                {
                    // Sin origenes configurados, se cierra por completo. Es mejor
                    // que dejar abierto por omision.
                    politica.AllowAnyHeader().AllowAnyMethod().WithOrigins();
                    return;
                }

                politica.WithOrigins(origenes)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        return servicios;
    }

    /// <summary>
    /// Swagger con los comentarios XML, que es lo que hace util la UI:
    /// sin ellos el panel solo muestra nombres de metodo.
    /// </summary>
    public static IServiceCollection AddSwagger(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        var archivoXml = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
        var rutaXml = Path.Combine(AppContext.BaseDirectory, archivoXml);

        servicios.AddEndpointsApiExplorer();

        servicios.AddSwaggerGen(opciones =>
        {
            if (File.Exists(rutaXml))
            {
                opciones.IncludeXmlComments(rutaXml);
            }

            opciones.SwaggerDoc("v1", new()
            {
                Title = "API de Transacciones",
                Version = "v1",
                Description =
                    "API de ejemplo. Muestra el reparto por capas del servicio: " +
                    "Core define los contratos, Services la logica, Data la persistencia, " +
                    "External los clientes salientes y WebApi la exposicion HTTP.",
            });
        });

        return servicios;
    }

    /// <summary>
    /// Newtonsoft en vez del serializador de System.Text.Json.
    /// <para>
    /// Se usa <c>ReferenceLoopHandling.Ignore</c> porque los grafos de objetos
    /// con referencias cruzadas (por ejemplo, solicitud -&gt; cliente -&gt;
    /// solicitudes) producen un <c>Self referencing loop has been detected</c> y la
    /// respuesta muere al serializar, con un 500 sin cuerpo util.
    /// </para>
    /// </summary>
    public static IServiceCollection AddNewtonsoftJson(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios
            .AddControllers()
            .AddNewtonsoftJson(opciones =>
            {
                opciones.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                opciones.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                opciones.SerializerSettings.DateFormatHandling = DateFormatHandling.IsoDateFormat;
            });

        return servicios;
    }
}