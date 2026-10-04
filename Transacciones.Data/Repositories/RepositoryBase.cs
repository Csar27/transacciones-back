using Microsoft.EntityFrameworkCore;
using Transacciones.Core.Models;
using Transacciones.CrossCutting.Model;

namespace Transacciones.Data.Repositories;

/// <summary>
/// Metodos genericos sobre el <see cref="DbContext"/>.
/// <para>
/// Se hereda, no se inyecta: los repositorios concretos son EF por herencia y
/// Dapper por composicion. Asi el mapeo EF y las consultas intensivas conviven
/// en la misma clase sin que el caller tenga que saber cual se uso.
/// </para>
/// </summary>
public abstract class RepositoryBase<TContexto>(TContexto contexto) where TContexto : DbContext
{
    protected TContexto Contexto { get; } = contexto;

    protected IQueryable<TEntity> Set<TEntity>() where TEntity : class => Contexto.Set<TEntity>();

    /// <summary>Consulta sin rastreo: solo lectura. Es la opcion por defecto.</summary>
    protected IQueryable<TEntity> SinRastreo<TEntity>() where TEntity : class =>
        Set<TEntity>().AsNoTracking();

    /// <summary>Consulta con rastreo, necesaria si la entidad se va a modificar.</summary>
    protected IQueryable<TEntity> ConRastreo<TEntity>() where TEntity : class =>
        Set<TEntity>().AsTracking();

    protected async Task<int> GuardarAsync(CancellationToken ct = default) =>
        await Contexto.SaveChangesAsync(ct).ConfigureAwait(false);

    /// <summary>
    /// Cuenta y pagina en una sola ida a la base.
    /// Sin esto, dos consultas (<c>Count</c> y despues la pagina) dan un total
    /// inconsistente si alguien inserta entre medias.
    /// </summary>
    protected async Task<PagedResult<TEntidad>> PaginarAsync<TEntidad>(
        IQueryable<TEntidad> consulta,
        SolicitudCriteria criteria,
        CancellationToken ct = default)
        where TEntidad : class
    {
        var limpio = criteria.Sanitizado();

        var total = await consulta.CountAsync(ct).ConfigureAwait(false);

        var items = await consulta
            .Skip((limpio.Pagina - 1) * limpio.TamanoPagina)
            .Take(limpio.TamanoPagina)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new PagedResult<TEntidad>(items, total, limpio.Pagina, limpio.TamanoPagina);
    }
}