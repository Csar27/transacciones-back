using Transacciones.CrossCutting.Enums;
using Transacciones.CrossCutting.Exceptions;
using Transacciones.CrossCutting.Models;

namespace Transacciones.Core.Models;

/// <summary>
/// Entidad de dominio. Es un POCO sin atributos de persistencia: el mapeo
/// vive en el <c>DbContext</c> (capa Data), que es donde corresponde.
/// <para>
/// Las transiciones de estado estan aqui, no en el controller. Si cada endpoint
/// compusiera su propio <c>if</c>, las reglas acabarian divergiendo.
/// </para>
/// </summary>
public sealed class Solicitud : AuditableEntity
{
    public Guid Id { get; set; }

    /// <summary>Codigo legible para el usuario. Es unico y va con prefijo y fecha.</summary>
    public string Codigo { get; set; } = string.Empty;

    public Guid ClienteId { get; set; }

    public decimal Monto { get; set; }

    public string Moneda { get; set; } = "PEN";

    public EnumEstadoSolicitud Estado { get; set; } = EnumEstadoSolicitud.Borrador;

    public DateTime FechaSolicitud { get; set; }

    public DateTime FechaVencimiento { get; set; }

    public string? Observacion { get; set; }

    public DateTime? FechaAprobacion { get; set; }

    public DateTime? FechaRechazo { get; set; }

    public string? MotivoRechazo { get; set; }

    /// <summary>Estados desde los que ya no se sale.</summary>
    public static IReadOnlyCollection<EnumEstadoSolicitud> EstadosTerminales { get; } =
        new[] { EnumEstadoSolicitud.Rechazada, EnumEstadoSolicitud.Vencida, EnumEstadoSolicitud.Anulada };

    /// <summary>
    /// Estados que el paso de vencimiento puede marcar como vencidos.
    /// <para>
    /// No es lo mismo que "no terminal": una solicitud Aprobada sigue abierta,
    /// pero el job no la toca. Por eso la lista es explicita en vez de
    /// calcularse como la negation de <see cref="EstadosTerminales"/>: una
    /// propiedad que dice "vencida" para un estado sobre el que nadie actua es
    /// una trampa para quien la lea.
    /// </para>
    /// </summary>
    public static IReadOnlyCollection<EnumEstadoSolicitud> EstadosVencenables { get; } =
        new[] { EnumEstadoSolicitud.Borrador, EnumEstadoSolicitud.Registrada };

    public bool EsTerminal => EstadosTerminales.Contains(Estado);

    /// <summary>
    /// True si el paso de vencimiento deberia marcar esta solicitud como
    /// vencida ahora mismo.
    /// </summary>
    public bool EstaVencida(DateTime ahora) =>
        FechaVencimiento < ahora && EstadosVencenables.Contains(Estado);

    public void Registrar(DateTime ahora) => Transicionar(EnumEstadoSolicitud.Registrada, ahora);

    public void Aprobar(DateTime ahora)
    {
        Transicionar(EnumEstadoSolicitud.Aprobada, ahora);
        FechaAprobacion = ahora;
    }

    public void Rechazar(string motivo, DateTime ahora)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new BusinessRuleException("SOL-004", "El motivo del rechazo es obligatorio.");
        }

        Transicionar(EnumEstadoSolicitud.Rechazada, ahora);
        FechaRechazo = ahora;
        MotivoRechazo = motivo.Trim();
    }

    public void Anular(DateTime ahora) => Transicionar(EnumEstadoSolicitud.Anulada, ahora);

    public void MarcarVencida(DateTime ahora) => Transicionar(EnumEstadoSolicitud.Vencida, ahora);

    /// <summary>
    /// Punto unico de control de transiciones. Si anadir una regla nueva de estado,
    /// se anade aqui y no repartida por los servicios.
    /// </summary>
    private void Transicionar(EnumEstadoSolicitud destino, DateTime ahora)
    {
        var permitida = EsTransicionValida(Estado, destino);

        if (!permitida)
        {
            throw new BusinessRuleException(
                "SOL-003",
                $"No se puede pasar de {Estado} a {destino}.");
        }

        Estado = destino;
    }

    /// <summary>
    /// Tabla de transiciones. Declararla explicitamente es preferible a un
    /// <c>switch</c> disperso: es la lista de reglas del dominio, en un sitio.
    /// </summary>
    private static bool EsTransicionValida(EnumEstadoSolicitud origen, EnumEstadoSolicitud destino) =>
        (origen, destino) switch
        {
            (EnumEstadoSolicitud.Borrador, EnumEstadoSolicitud.Registrada) => true,
            (EnumEstadoSolicitud.Borrador, EnumEstadoSolicitud.Anulada) => true,
            (EnumEstadoSolicitud.Registrada, EnumEstadoSolicitud.Aprobada) => true,
            (EnumEstadoSolicitud.Registrada, EnumEstadoSolicitud.Rechazada) => true,
            (EnumEstadoSolicitud.Registrada, EnumEstadoSolicitud.Vencida) => true,
            (EnumEstadoSolicitud.Registrada, EnumEstadoSolicitud.Anulada) => true,
            (EnumEstadoSolicitud.Aprobada, EnumEstadoSolicitud.Anulada) => true,
            _ => false,
        };
}