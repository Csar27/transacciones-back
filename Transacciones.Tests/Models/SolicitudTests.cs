using FluentAssertions;
using Transacciones.Core.Models;
using Transacciones.CrossCutting.Enums;
using Transacciones.CrossCutting.Exceptions;
using Xunit;

namespace Transacciones.Tests.Models;

/// <summary>
/// Pruebas de la maquina de estados de la entidad.
/// <para>
/// Van aqui, no en el servicio: las transiciones son reglas de la entidad. Si
/// se probaran a traves del servicio, haria falta montar un repositorio mock
/// para comprobar algo que ocurre enteramente en memoria.
/// </para>
/// </summary>
public sealed class SolicitudTests
{
    private static Solicitud NuevaSolicitud() => new()
    {
        Id = Guid.NewGuid(),
        Codigo = "SOL-2026-000001",
        ClienteId = Guid.NewGuid(),
        Monto = 100.00m,
        Moneda = "PEN",
        FechaSolicitud = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        FechaVencimiento = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        Estado = EnumEstadoSolicitud.Borrador,
    };

    [Fact]
    public void Registrar_desde_Borrador_pasa_a_Registrada()
    {
        var solicitud = NuevaSolicitud();

        solicitud.Registrar(DateTime.UtcNow);

        solicitud.Estado.Should().Be(EnumEstadoSolicitud.Registrada);
    }

    [Fact]
    public void Registrar_desde_Registrada_falla_porque_la_transicion_no_existe()
    {
        var solicitud = NuevaSolicitud();
        solicitud.Registrar(DateTime.UtcNow);

        var accion = () => solicitud.Registrar(DateTime.UtcNow);

        accion.Should().Throw<BusinessRuleException>()
            .Which.Codigo.Should().Be("SOL-003");
    }

    [Fact]
    public void Aprobar_registra_la_fecha_de_aprobacion()
    {
        var solicitud = NuevaSolicitud();
        solicitud.Registrar(DateTime.UtcNow);

        var momento = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);
        solicitud.Aprobar(momento);

        solicitud.Estado.Should().Be(EnumEstadoSolicitud.Aprobada);
        solicitud.FechaAprobacion.Should().Be(momento);
    }

    [Fact]
    public void Aprobar_sin_registrar_falla()
    {
        var solicitud = NuevaSolicitud();

        var accion = () => solicitud.Aprobar(DateTime.UtcNow);

        accion.Should().Throw<BusinessRuleException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Rechazar_sin_motivo_falla(string? motivo)
    {
        var solicitud = NuevaSolicitud();
        solicitud.Registrar(DateTime.UtcNow);

        var accion = () => solicitud.Rechazar(motivo!, DateTime.UtcNow);

        accion.Should().Throw<BusinessRuleException>()
            .Which.Codigo.Should().Be("SOL-004");
    }

    [Fact]
    public void Rechazar_guarda_el_motivo_sin_espacios_sobrantes()
    {
        var solicitud = NuevaSolicitud();
        solicitud.Registrar(DateTime.UtcNow);

        solicitud.Rechazar("  Documento incompleto  ", DateTime.UtcNow);

        solicitud.Estado.Should().Be(EnumEstadoSolicitud.Rechazada);
        solicitud.MotivoRechazo.Should().Be("Documento incompleto");
    }

    [Fact]
    public void Anular_desde_Registrada_es_valido()
    {
        var solicitud = NuevaSolicitud();
        solicitud.Registrar(DateTime.UtcNow);

        solicitud.Anular(DateTime.UtcNow);

        solicitud.Estado.Should().Be(EnumEstadoSolicitud.Anulada);
    }

    [Theory]
    [InlineData(EnumEstadoSolicitud.Rechazada, true)]
    [InlineData(EnumEstadoSolicitud.Vencida, true)]
    [InlineData(EnumEstadoSolicitud.Anulada, true)]
    [InlineData(EnumEstadoSolicitud.Registrada, false)]
    [InlineData(EnumEstadoSolicitud.Aprobada, false)]
    public void EsTerminal_refleja_el_caracter_final_del_estado(
        EnumEstadoSolicitud estado,
        bool esperado)
    {
        var solicitud = NuevaSolicitud();
        solicitud.Estado = estado;

        solicitud.EsTerminal.Should().Be(esperado);
    }

    [Theory]
    [InlineData(EnumEstadoSolicitud.Rechazada)]
    [InlineData(EnumEstadoSolicitud.Vencida)]
    [InlineData(EnumEstadoSolicitud.Anulada)]
    public void EstaVencida_es_falso_si_la_solicitud_ya_esta_terminada(EnumEstadoSolicitud estado)
    {
        var solicitud = NuevaSolicitud();
        solicitud.Estado = estado;
        solicitud.FechaVencimiento = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Un estado terminal no vuelve a vencerse, aunque la fecha ya haya pasado.
        solicitud.EstaVencida(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().BeFalse();
    }

    [Fact]
    public void EstaVencida_es_falso_en_Aprobada_porque_el_job_no_la_toca()
    {
        var solicitud = NuevaSolicitud();
        solicitud.Estado = EnumEstadoSolicitud.Aprobada;
        solicitud.FechaVencimiento = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Aprobada NO es terminal, pero tampoco es un estado que el paso de
        // vencimiento procese. Por eso la lista de vencenables es explicita.
        solicitud.EstaVencida(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(EnumEstadoSolicitud.Borrador)]
    [InlineData(EnumEstadoSolicitud.Registrada)]
    public void EstaVencida_es_cierto_si_la_vigencia_ya_paso_y_el_estado_es_vencenable(
        EnumEstadoSolicitud estado)
    {
        var solicitud = NuevaSolicitud();
        solicitud.Estado = estado;
        solicitud.FechaVencimiento = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        solicitud.EstaVencida(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().BeTrue();
    }

    [Fact]
    public void EstaVencida_es_falso_si_la_vigencia_aun_no_ha_pasado()
    {
        var solicitud = NuevaSolicitud();
        solicitud.FechaVencimiento = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        solicitud.EstaVencida(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            .Should().BeFalse();
    }

    [Fact]
    public void EstadosTerminales_contiene_los_tres_estados_finales()
    {
        Solicitud.EstadosTerminales.Should().BeEquivalentTo(new[]
        {
            EnumEstadoSolicitud.Rechazada,
            EnumEstadoSolicitud.Vencida,
            EnumEstadoSolicitud.Anulada,
        });
    }
}