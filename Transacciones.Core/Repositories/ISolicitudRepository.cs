using Transacciones.Core.Models;
using Transacciones.CrossCutting.Model;

namespace Transacciones.Core.Repositories;

/// <summary>
/// Contrato de persistencia de Solicitud. La implementacion (EF Core + Dapper)
/// vive en Transacciones.Data.
/// <para>
/// Los metodos reciben <see cref="CancellationToken"/> porque las consultas
/// pueden tardar y cancelar un request debe cancelar su consulta.
/// </para>
/// </summary>
public interface ISolicitudRepository
{
    /// <summary>Devuelve una pagina de resultados con el total filtrado.</summary>
    Task<PagedResult<Solicitud>> ListarAsync(SolicitudCriteria criteria, CancellationToken ct = default);

    /// <summary>Devuelve la entidad rastreada, o null si no existe.</summary>
    Task<Solicitud?> ObtenerAsync(Guid id, CancellationToken ct = default);

    /// <summary>Busca por codigo, que es la clave legible para el usuario.</summary>
    Task<Solicitud?> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default);

    /// <summary>
    /// Calcula el siguiente codigo correlativo del anio.
    /// Va al base de datos porque dos peticiones simultaneas deben obtener
    /// codigos distintos.
    /// </summary>
    Task<string> GenerarCodigoAsync(CancellationToken ct = default);

    Task AgregarAsync(Solicitud solicitud, CancellationToken ct = default);

    /// <summary>Confirma los cambios pendientes del contexto.</summary>
    Task GuardarAsync(CancellationToken ct = default);

    /// <summary>
    /// Paso masivo: marca como vencidas las solicitudes no terminales cuya
    /// vigencia se cumplio antes de <paramref name="fechaCorte"/>.
    /// Devuelve cuantas se actualizaron.
    /// </summary>
    Task<int> MarcarVencidasAsync(DateTime fechaCorte, CancellationToken ct = default);
}