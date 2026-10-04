using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Transacciones.Core.Models;
using Transacciones.Core.Repositories;
using Transacciones.Core.Services;
using Transacciones.CrossCutting.Enums;
using Transacciones.CrossCutting.Exceptions;
using Transacciones.CrossCutting.Model;
using Transacciones.Services.Services;
using Xunit;

namespace Transacciones.Tests.Services;

/// <summary>
/// Pruebas del servicio con las dependencias simuladas.
/// <para>
/// El servicio no sabe que hay base de datos: recibe <see cref="ISolicitudRepository"/>.
/// Eso es lo que permite comprobarlo entero sin levantar SQL Server.
/// </para>
/// </summary>
public sealed class SolicitudServiceTests
{
    private readonly Mock<ISolicitudRepository> _repositorio = new();
    private readonly Mock<INotificacionService> _notificaciones = new();
    private readonly CurrentRequestContext _contexto = new()
    {
        CorrelationId = "prueba-123",
        UserId = "usuario.prueba",
    };

    private SolicitudService CrearServicio() =>
        new(
            _repositorio.Object,
            _notificaciones.Object,
            _contexto,
            NullLogger<SolicitudService>.Instance);

    private static Solicitud NuevaSolicitud(EnumEstadoSolicitud estado = EnumEstadoSolicitud.Registrada)
    {
        var solicitud = new Solicitud
        {
            Id = Guid.NewGuid(),
            Codigo = "SOL-2026-000001",
            ClienteId = Guid.NewGuid(),
            Monto = 250.00m,
            Moneda = "PEN",
            FechaSolicitud = DateTime.UtcNow,
            FechaVencimiento = DateTime.UtcNow.AddDays(30),
            Estado = estado,
        };

        return solicitud;
    }

    [Fact]
    public async Task ObtenerAsync_lanza_no_encontrada_si_no_existe()
    {
        var id = Guid.NewGuid();
        _repositorio.Setup(r => r.ObtenerAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Solicitud?)null);

        var servicio = CrearServicio();

        var accion = () => servicio.ObtenerAsync(id);

        await accion.Should().ThrowAsync<Core.Exceptions.SolicitudNoEncontradaException>();
    }

    [Fact]
    public async Task CrearAsync_asigna_codigo_y_deja_la_solicitud_registrada()
    {
        _repositorio.Setup(r => r.GenerarCodigoAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("SOL-2026-000042");

        var idGuardada = Guid.NewGuid();
        Solicitud? capturada = null;

        _repositorio.Setup(r => r.AgregarAsync(It.IsAny<Solicitud>(), It.IsAny<CancellationToken>()))
            .Callback<Solicitud, CancellationToken>((s, _) => capturada = s)
            .Returns(Task.CompletedTask);

        _repositorio.Setup(r => r.GuardarAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var servicio = CrearServicio();

        var resultado = await servicio.CrearAsync(new CrearSolicitudDto
        {
            ClienteId = Guid.NewGuid(),
            Monto = 500m,
            Moneda = "pen",
            VigenciaDias = 15,
        });

        resultado.Codigo.Should().Be("SOL-2026-000042");

        // La moneda se normaliza a mayusculas: "pen" y "PEN" no pueden ser dos
        // monedas distintas en el mismo dato.
        resultado.Moneda.Should().Be("PEN");
        resultado.Estado.Should().Be(EnumEstadoSolicitud.Registrada);
        resultado.CreadoPor.Should().Be("usuario.prueba");
        resultado.FechaVencimiento.Should().BeAfter(resultado.FechaSolicitud);

        _repositorio.Verify(r => r.GuardarAsync(It.IsAny<CancellationToken>()), Times.Once);
        capturada.Should().NotBeNull();
        capturada!.Codigo.Should().Be("SOL-2026-000042");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task CrearAsync_con_monto_no_positivo_falla_sin_tocar_la_base(int monto)
    {
        var servicio = CrearServicio();

        var accion = () => servicio.CrearAsync(new CrearSolicitudDto
        {
            ClienteId = Guid.NewGuid(),
            Monto = monto,
        });

        (await accion.Should().ThrowAsync<BusinessRuleException>())
            .Which.Codigo.Should().Be("SOL-001");

        // Lo importante: no se llega a escribir nada.
        _repositorio.Verify(r => r.AgregarAsync(It.IsAny<Solicitud>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(400)]
    public async Task CrearAsync_con_vigencia_fuera_de_rango_falla(int vigencia)
    {
        var servicio = CrearServicio();

        var accion = () => servicio.CrearAsync(new CrearSolicitudDto
        {
            ClienteId = Guid.NewGuid(),
            Monto = 100m,
            VigenciaDias = vigencia,
        });

        (await accion.Should().ThrowAsync<BusinessRuleException>())
            .Which.Codigo.Should().Be("SOL-002");
    }

    [Fact]
    public async Task AprobarAsync_cambia_el_estado_y_guarda()
    {
        var solicitud = NuevaSolicitud();
        _repositorio.Setup(r => r.ObtenerAsync(solicitud.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(solicitud);
        _repositorio.Setup(r => r.GuardarAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var servicio = CrearServicio();

        var resultado = await servicio.AprobarAsync(solicitud.Id);

        resultado.Estado.Should().Be(EnumEstadoSolicitud.Aprobada);
        _repositorio.Verify(r => r.GuardarAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AprobarAsync_respeta_la_regla_de_transicion_del_dominio()
    {
        // Esta en Aprobada: volver a aprobar es una transicion invalida.
        var solicitud = NuevaSolicitud(EnumEstadoSolicitud.Aprobada);
        _repositorio.Setup(r => r.ObtenerAsync(solicitud.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(solicitud);

        var servicio = CrearServicio();

        var accion = () => servicio.AprobarAsync(solicitud.Id);

        (await accion.Should().ThrowAsync<BusinessRuleException>())
            .Which.Codigo.Should().Be("SOL-003");

        _repositorio.Verify(r => r.GuardarAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Si_la_notificacion_falla_el_cambio_de_estado_no_se_pierde()
    {
        // Esta es la prueba que justifica el try/catch del servicio: el aviso
        // falla, pero el estado ya esta guardado y no debe deshacerse.
        var solicitud = NuevaSolicitud();
        _repositorio.Setup(r => r.ObtenerAsync(solicitud.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(solicitud);
        _repositorio.Setup(r => r.GuardarAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _notificaciones
            .Setup(n => n.NotificarCambioEstadoAsync(It.IsAny<Solicitud>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("el servidor de correo no responde"));

        var servicio = CrearServicio();

        var resultado = await servicio.AprobarAsync(solicitud.Id);

        resultado.Estado.Should().Be(EnumEstadoSolicitud.Aprobada);
        _repositorio.Verify(r => r.GuardarAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnularAsync_desde_Registrada_deja_la_solicitud_anulada()
    {
        var solicitud = NuevaSolicitud();
        _repositorio.Setup(r => r.ObtenerAsync(solicitud.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(solicitud);
        _repositorio.Setup(r => r.GuardarAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var servicio = CrearServicio();

        var resultado = await servicio.AnularAsync(solicitud.Id);

        resultado.Estado.Should().Be(EnumEstadoSolicitud.Anulada);

        // Anular no notifica: es una accion interna, no un hito para el cliente.
        _notificaciones.Verify(
            n => n.NotificarCambioEstadoAsync(It.IsAny<Solicitud>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ListarAsync_sanitiza_el_criterio_antes_de_llegar_al_repositorio()
    {
        _repositorio.Setup(r => r.ListarAsync(It.IsAny<SolicitudCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PagedResult<Solicitud>.Vacia(1, 20));

        var servicio = CrearServicio();

        await servicio.ListarAsync(new SolicitudCriteria { Pagina = -5, TamanoPagina = 5000 });

        _repositorio.Verify(r => r.ListarAsync(
            It.Is<SolicitudCriteria>(c => c.Pagina == 1 && c.TamanoPagina == 200),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}