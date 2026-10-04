using Microsoft.EntityFrameworkCore;
using Transacciones.Core.Models;

namespace Transacciones.Data.Persistence;

/// <summary>
/// Contexto de la base normal (AFF).
/// <para>
/// Se mapea la entidad a mano con Fluent API en vez de decorar el POCO con
/// atributos. La consecuencia util es que <c>Transacciones.Core</c> no depende
/// de EF Core: la entidad es dominio puro y el mapeo es de la capa de datos.
/// </para>
/// </summary>
public class SolicitudDbContext(DbContextOptions<SolicitudDbContext> opciones) : DbContext(opciones)
{
    /// <summary>
    /// Trigger de auditoria que puede existir en la base.
    /// <para>
    /// EF Core 7+ anade <c>OUTPUT INSERTED</c> a las sentencias de escritura para
    /// leer de vuelta lo que cambio. SQL Server PROHIBE la clausula <c>OUTPUT</c>
    /// sobre una tabla con triggers activos, y falla con "Could not save changes
    /// because the target table has database triggers".
    /// </para>
    /// <para>
    /// Declarar el trigger aqui hace que EF use <c>OUTPUT ... INTO</c>, que si esta
    /// permitido. Y da igual que el trigger exista o no: si ya se elimino de la
    /// base, <c>OUTPUT INTO</c> sigue funcionando igual. Es una declaracion que no
    /// obliga a que el objeto exista, y por eso no hay que depender de que alguien
    /// se acuerde de quitar el trigger antes de desplegar.
    /// </para>
    /// </summary>
    private const string TriggerAuditoria = "trg_Solicitud_Auditoria";

    public DbSet<Solicitud> Solicitudes => Set<Solicitud>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Solicitud>(entidad =>
        {
            entidad.ToTable(
                "Solicitud",
                "dbo",
                tabla => tabla.HasTrigger(TriggerAuditoria));

            entidad.HasKey(e => e.Id);

            entidad.Property(e => e.Id).ValueGeneratedNever();
            entidad.Property(e => e.Codigo).HasMaxLength(30).IsRequired();
            entidad.Property(e => e.Moneda).HasMaxLength(3).IsFixedLength(false).IsRequired();
            entidad.Property(e => e.Estado).HasColumnType("tinyint").IsRequired();
            entidad.Property(e => e.Monto).HasColumnType("decimal(18,2)").IsRequired();
            entidad.Property(e => e.Observacion).HasMaxLength(500);
            entidad.Property(e => e.MotivoRechazo).HasMaxLength(500);
            entidad.Property(e => e.CreadoPor).HasMaxLength(100).IsRequired();
            entidad.Property(e => e.ModificadoPor).HasMaxLength(100);

            entidad.Property(e => e.FechaSolicitud).HasColumnType("datetime2(0)");
            entidad.Property(e => e.FechaVencimiento).HasColumnType("datetime2(0)");
            entidad.Property(e => e.FechaCreacion).HasColumnType("datetime2(0)");
            entidad.Property(e => e.FechaModificacion).HasColumnType("datetime2(0)");
            entidad.Property(e => e.FechaAprobacion).HasColumnType("datetime2(0)");
            entidad.Property(e => e.FechaRechazo).HasColumnType("datetime2(0)");

            entidad.HasIndex(e => e.Estado);
            entidad.HasIndex(e => e.ClienteId);

            // Indice COMPUESTO y filtrado a la vez. El job de vencimiento busca por
            // FechaVencimiento sobre lo que aun no esta resuelto (0 Borrador,
            // 1 Registrada), asi que el indice cubre exactamente lo que consulta.
            // Sobre toda la tabla seria mas grande a cambio de filas que nunca
            // se van a leer.
            //
            // El filtro va en forma POSITIVA a proposito: el predicado de un
            // indice filtrado no admite NOT en este motor (error de sintaxis).
            // Ademas, enumerar lo que SI se consulta documenta la intencion
            // mejor que enumerar los estados que se descartan.
            entidad.HasIndex(e => new { e.FechaVencimiento, e.Estado })
                .HasFilter("[Estado] IN (0, 1)")
                .HasDatabaseName("IX_Solicitud_Vencimiento_NoTerminales");
        });
    }
}