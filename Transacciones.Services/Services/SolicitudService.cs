using Microsoft.Extensions.Logging;
using Transacciones.Core.Models;
using Transacciones.Core.Repositories;
using Transacciones.Core.Services;
using Transacciones.CrossCutting.Exceptions;
using Transacciones.CrossCutting.Model;
using Transacciones.Services.Services;

namespace Transacciones.Services.Services;

/// <summary>
/// Casos de uso de Solicitud.
/// <para>
/// Aqui solo hay reglas de negocio y orquestacion. Las decisiones de persistencia
/// son del repositorio y las reglas de transicion de estado son de la entidad.
/// Repartirlas asi es lo que permite probar el servicio sin base de datos.
/// </para>
/// </summary>
public sealed class SolicitudService(
    ISolicitudRepository repositorio,
    INotificacionService notificaciones,
    ICurrentRequestContext contexto,
    ILogger<SolicitudService> logger)
    : DataServiceBase(logger), ISolicitudService
{
    /// <summary>
    /// Usuario que se escribe cuando la peticion no trae <c>X_userId</c>. Es el
    /// mismo valor para crear y para modificar, para que "quien lo hizo" tenga
    /// una sola respuesta en toda la tabla.
    /// </summary>
    private const string UsuarioPorDefecto = "sistema";

    public Task<PagedResult<Solicitud>> ListarAsync(
        SolicitudCriteria criteria,
        CancellationToken ct = default) =>
        repositorio.ListarAsync(criteria.Sanitizado(), ct);

    public async Task<Solicitud> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var solicitud = await repositorio.ObtenerAsync(id, ct).ConfigureAwait(false);

        if (solicitud is null)
        {
            throw new Core.Exceptions.SolicitudNoEncontradaException(id);
        }

        return solicitud;
    }

    public async Task<Solicitud> CrearAsync(CrearSolicitudDto dto, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        if (dto.Monto <= 0m)
        {
            throw new BusinessRuleException("SOL-001", "El monto debe ser mayor que cero.");
        }

        if (dto.VigenciaDias is < 1 or > 365)
        {
            throw new BusinessRuleException("SOL-002", "La vigencia debe estar entre 1 y 365 dias.");
        }

        var ahora = DateTime.UtcNow;

        var solicitud = new Solicitud
        {
            Id = Guid.NewGuid(),
            Codigo = await repositorio.GenerarCodigoAsync(ct).ConfigureAwait(false),
            ClienteId = dto.ClienteId,
            Monto = dto.Monto,
            Moneda = dto.Moneda.ToUpperInvariant(),
            Observacion = dto.Observacion,
            FechaSolicitud = ahora,
            FechaVencimiento = ahora.AddDays(dto.VigenciaDias),
            FechaCreacion = ahora,
            CreadoPor = contexto.UserId ?? UsuarioPorDefecto,
        };

        // Nace registrada: el front no crea borradores a proposito.
        solicitud.Registrar(ahora);

        await repositorio.AgregarAsync(solicitud, ct).ConfigureAwait(false);
        await repositorio.GuardarAsync(ct).ConfigureAwait(false);

        Logger.LogInformation(
            "Solicitud {Codigo} creada para el cliente {ClienteId} por {Usuario}",
            solicitud.Codigo,
            solicitud.ClienteId,
            solicitud.CreadoPor);

        return solicitud;
    }

    public async Task<Solicitud> AprobarAsync(Guid id, CancellationToken ct = default)
    {
        var solicitud = await ObtenerAsync(id, ct).ConfigureAwait(false);
        var ahora = DateTime.UtcNow;

        solicitud.Aprobar(ahora);
        MarcarModificada(solicitud, ahora);

        await repositorio.GuardarAsync(ct).ConfigureAwait(false);
        await NotificarAsync(solicitud, ct).ConfigureAwait(false);

        return solicitud;
    }

    public async Task<Solicitud> RechazarAsync(
        Guid id,
        string motivo,
        CancellationToken ct = default)
    {
        var solicitud = await ObtenerAsync(id, ct).ConfigureAwait(false);
        var ahora = DateTime.UtcNow;

        solicitud.Rechazar(motivo, ahora);
        MarcarModificada(solicitud, ahora);

        await repositorio.GuardarAsync(ct).ConfigureAwait(false);
        await NotificarAsync(solicitud, ct).ConfigureAwait(false);

        return solicitud;
    }

    public async Task<Solicitud> AnularAsync(Guid id, CancellationToken ct = default)
    {
        var solicitud = await ObtenerAsync(id, ct).ConfigureAwait(false);
        var ahora = DateTime.UtcNow;

        solicitud.Anular(ahora);
        MarcarModificada(solicitud, ahora);

        await repositorio.GuardarAsync(ct).ConfigureAwait(false);

        return solicitud;
    }

    /// <summary>
    /// Estampa los campos de auditoria de una modificacion.
    /// <para>
    /// Lo hace el servicio y no un trigger de la base. Un <c>AFTER UPDATE</c> que
    /// reescribe la fila que el mismo disparo solo funciona porque
    /// <c>RECURSIVE_TRIGGERS</c> esta apagado: en cuanto alguien lo activa en la
    /// base, el trigger se vuelve a disparar sobre si mismo y agota el limite de
    /// anidamiento.
    /// </para>
    /// <para>
    /// El fallo es dificil de ver: ocurre al actualizar y nunca al insertar, asi
    /// que crear solicitudes funciona, aprobar no, y al cliente le llega un 500
    /// sin detalle. Ademas la estampa de auditoria vivia solo en SQL, asi que
    /// desde codigo <c>FechaModificacion</c> llegaba siempre nula.
    /// </para>
    /// </summary>
    private void MarcarModificada(Solicitud solicitud, DateTime ahora)
    {
        solicitud.FechaModificacion = ahora;
        solicitud.ModificadoPor = contexto.UserId ?? UsuarioPorDefecto;
    }

    /// <summary>
    /// Notifica sin dejar tumbar la operacion principal.
    /// <para>
    /// Un aviso fallido no debe deshacer un cambio de estado ya guardado: si
    /// espera, el usuario pierde la aprobacion porque el servidor de correo no
    /// respondio. Se registra el fallo y la peticion sigue.
    /// </para>
    /// </summary>
    private async Task NotificarAsync(Solicitud solicitud, CancellationToken ct)
    {
        try
        {
            await notificaciones.NotificarCambioEstadoAsync(solicitud, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // Filtro importante: NO basta con excluir OperationCanceledException.
            //
            // HttpClient lanza TaskCanceledException —que hereda de
            // OperationCanceledException— cuando se agota SU timeout. Eso NO es
            // que el cliente se vaya: es que el servicio de notificaciones no
            // respondio. Con el filtro anterior esa excepcion escapaba, y el
            // resultado era: estado guardado en la base y un 500 en pantalla.
            //
            //   - Si el cliente cancelo, ct tambien: nadie va a leer la
            //     respuesta, se registra y ya esta.
            //   - Si ct NO esta cancelado, es un timeout del servicio externo: el
            //     cambio ya esta guardado y el aviso se pierde, pero la
            //     operacion principal es un exito.
            Logger.LogError(
                ex,
                "No se pudo notificar el cambio de estado de {Codigo}. El estado SI quedo guardado.",
                solicitud.Codigo);
        }
    }
}