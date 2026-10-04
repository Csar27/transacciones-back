using Quartz;
using Transacciones.WebApi.Jobs;

namespace Transacciones.WebApi.Schedular;

/// <summary>
/// Arranque del planificador de Quartz.
/// <para>
/// Los jobs se registran aqui, no dentro de los servicios: un servicio no debe
/// saber que existe Quartz. Si lo supiera, no se podria ejecutar un caso de uso
/// sin levantar el planificador entero.
/// </para>
/// <para>
/// La clase del trabajo vive en <c>Jobs/</c>. Aqui solo se declara cuando y con
/// que frecuencia corre, que es configuracion, no codigo de negocio.
/// </para>
/// </summary>
public static class SchedulerExtensions
{
    /// <summary>Registra Quartz y el job de vencimiento.</summary>
    public static IServiceCollection RegisterBackgroundTask(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        servicios.AddQuartz(registro =>
        {
            registro.UseSimpleTypeLoader();
            registro.UseInMemoryStore();

            registro.AddJob<SolicitudVencidaJob>(builder => builder
                .WithIdentity("solicitud-vencida")
                // StoreDurably evita que el trigger se borre si el job no
                // esta corriendo en ese momento.
                .StoreDurably());

            registro.AddTrigger(disparador => disparador
                .ForJob("solicitud-vencida")
                .WithSimpleSchedule(plan => plan
                    .WithIntervalInHours(1)
                    .RepeatForever())
                .StartNow());
        });

        servicios.AddQuartzHostedService(registro =>
        {
            // Espera a que terminen los jobs en curso antes de apagar, para no
            // cortar un paso de vencimiento a medias. Es una PROPIEDAD, no un
            // metodo: llamarla con parentesis no compila.
            registro.WaitForJobsToComplete = true;
        });

        return servicios;
    }
}