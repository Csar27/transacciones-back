using Microsoft.EntityFrameworkCore;
using Transacciones.Core.Models;
using Transacciones.Core.Repositories;
using Transacciones.CrossCutting.Model;
using Transacciones.Data.Persistence;
using Transacciones.Data.Repositories;

namespace Transacciones.Data.Repositories;

/// <summary>
/// Implementacion de <see cref="ISolicitudRepository"/>.
/// <para>
/// Hereda de <see cref="RepositoryBase{TContexto}"/> para las consultas EF, pero
/// compone un <see cref="SqlExecutor"/> para lo que conviene en SQL crudo: el
/// correlativo de codigo y el paso masivo de vencimientos.
/// </para>
/// </summary>
public sealed class SolicitudRepository(
    SolicitudDbContext contexto,
    SqlExecutor sql)
    : RepositoryBase<SolicitudDbContext>(contexto), ISolicitudRepository
{
    public async Task<PagedResult<Solicitud>> ListarAsync(
        SolicitudCriteria criteria,
        CancellationToken ct = default)
    {
        var limpio = criteria.Sanitizado();

        var consulta = SinRastreo<Solicitud>().AsQueryable();

        if (!string.IsNullOrWhiteSpace(limpio.Codigo))
        {
            var patron = $"%{limpio.Codigo.Trim()}%";
            consulta = consulta.Where(s => EF.Functions.Like(s.Codigo, patron));
        }

        if (limpio.ClienteId.HasValue)
        {
            consulta = consulta.Where(s => s.ClienteId == limpio.ClienteId.Value);
        }

        if (limpio.Estado.HasValue)
        {
            consulta = consulta.Where(s => s.Estado == limpio.Estado.Value);
        }

        if (limpio.Desde.HasValue)
        {
            consulta = consulta.Where(s => s.FechaSolicitud >= limpio.Desde.Value);
        }

        if (limpio.Hasta.HasValue)
        {
            consulta = consulta.Where(s => s.FechaSolicitud <= limpio.Hasta.Value);
        }

        return await PaginarAsync(
                consulta.OrderByDescending(s => s.FechaSolicitud).ThenBy(s => s.Codigo),
                limpio,
                ct)
            .ConfigureAwait(false);
    }

    public Task<Solicitud?> ObtenerAsync(Guid id, CancellationToken ct = default) =>
        // ConRastreo: el servicio va a modificar el estado y EF necesita vigilarla.
        ConRastreo<Solicitud>().FirstOrDefaultAsync(s => s.Id == id, ct);

    public Task<Solicitud?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default) =>
        SinRastreo<Solicitud>().FirstOrDefaultAsync(s => s.Codigo == codigo, ct);

    /// <inheritdoc />
    /// <remarks>
    /// Delega en <c>usp_Solicitud_GenerarCodigo</c>, que usa la SEQUENCE
    /// <c>dbo.SeqSolicitudCodigo</c>.
    /// <para>
    /// No se calcula aqui con "MAX(Codigo) + 1" a proposito: dos peticiones
    /// simultaneas leerian el mismo maximo y generarian el mismo codigo. El
    /// indice unico rechazaria la segunda, y el usuario recibiria un error en una
    /// operacion que era perfectamente valida.
    /// </para>
    /// </remarks>
    public async Task<string> GenerarCodigoAsync(CancellationToken ct = default)
    {
        var resultado = await sql.ConsultarAsync<ResultadoGenerarCodigo>(
                "EXEC dbo.usp_Solicitud_GenerarCodigo @Anio = @Anio;",
                new { Anio = (short)DateTime.UtcNow.Year },
                ct)
            .ConfigureAwait(false);

        var codigo = resultado.SingleOrDefault()?.Codigo;

        return string.IsNullOrWhiteSpace(codigo)
            ? throw new InvalidOperationException("No se pudo generar el codigo de solicitud.")
            : codigo;
    }

    /// <summary>Fila que devuelve el procedimiento almacenado.</summary>
    private sealed class ResultadoGenerarCodigo
    {
        public string Codigo { get; set; } = string.Empty;
    }

    public async Task AgregarAsync(Solicitud solicitud, CancellationToken ct = default) =>
        await Contexto.Set<Solicitud>().AddAsync(solicitud, ct).ConfigureAwait(false);

    // OJO: sin "new", esta linea se llama a si misma en recursion infinita
    // (misma firma que el metodo heredado) y revienta la pila.
    public new Task GuardarAsync(CancellationToken ct = default) => base.GuardarAsync(ct);

    /// <inheritdoc />
    /// <remarks>
    /// Va por SQL crudo y no por EF. Recorrer fila por fila cargaria miles de
    /// entradas en el change tracker sin ganar nada: la sentencia afecta a las
    /// filas directamente.
    /// </remarks>
    public async Task<int> MarcarVencidasAsync(DateTime fechaCorte, CancellationToken ct = default)
    {
        const string consulta = """
            UPDATE dbo.Solicitud
               SET Estado = 4,
                   FechaModificacion = @FechaModificacion
             WHERE Estado IN (0, 1)
               AND FechaVencimiento < @FechaCorte;
            """;

        return await sql.EjecutarAsync(
                consulta,
                new { FechaCorte = fechaCorte, FechaModificacion = DateTime.UtcNow },
                ct)
            .ConfigureAwait(false);
    }
}