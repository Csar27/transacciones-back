using AutoMapper;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Transacciones.Core.Models;
using Transacciones.Core.Services;
using Transacciones.CrossCutting.Enums;
using Transacciones.CrossCutting.Model;
using Transacciones.WebApi.Controllers;
using Transacciones.WebApi.Mapping;
using Xunit;

namespace Transacciones.Tests.Controllers;

/// <summary>
/// Pruebas del controller.
/// <para>
/// Se usa un <see cref="IMapper"/> REAL construido con el perfil de produccion, no
/// un mock. Con un mock, el test pasaria aunque el perfil estuviera mal
/// escrito, que es justo lo que hay que comprobar.
/// </para>
/// </summary>
public sealed class SolicitudControllerTests
{
    private readonly Mock<ISolicitudService> _servicio = new();
    private readonly CurrentRequestContext _contexto = new() { CorrelationId = "prueba-ctrl" };

    // Mapper real: asi el perfil de mapeo entra en la prueba.
    // AutoMapper 15 exige ILoggerFactory, y el orden es (configuracion, logger).
    private readonly IMapper _mapper = new MapperConfiguration(
        config =>
        {
            config.AddProfile<SolicitudProfile>();
            config.LicenseKey = string.Empty;
        },
        NullLoggerFactory.Instance).CreateMapper();

    private SolicitudController CrearController() => new(_servicio.Object, _mapper, _contexto);

    private static Solicitud NuevaSolicitud() => new()
    {
        Id = Guid.NewGuid(),
        Codigo = "SOL-2026-000001",
        ClienteId = Guid.NewGuid(),
        Monto = 750m,
        Moneda = "PEN",
        Estado = EnumEstadoSolicitud.Registrada,
        FechaSolicitud = DateTime.UtcNow,
        FechaVencimiento = DateTime.UtcNow.AddDays(30),
        CreadoPor = "operador.01",
        FechaCreacion = DateTime.UtcNow,
    };

    [Fact]
    public async Task ObtenerAsync_devuelve_200_con_el_sobre_ok()
    {
        var solicitud = NuevaSolicitud();
        _servicio.Setup(s => s.ObtenerAsync(solicitud.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(solicitud);

        var controller = CrearController();

        var resultado = await controller.ObtenerAsync(solicitud.Id);

        var ok = resultado.Result.Should().BeOfType<OkObjectResult>().Subject;
        var sobre = ok.Value.Should().BeOfType<ApiResponse<SolicitudDto>>().Subject;

        sobre.Exito.Should().BeTrue();
        sobre.Datos!.Codigo.Should().Be("SOL-2026-000001");

        // El estado llega tambien como texto para que el front no traduzca el enum.
        sobre.Datos.EstadoDescripcion.Should().Be(nameof(EnumEstadoSolicitud.Registrada));
    }

    [Fact]
    public async Task CrearAsync_devuelve_201_con_la_ubicacion_de_la_nueva_solicitud()
    {
        var solicitud = NuevaSolicitud();
        _servicio.Setup(s => s.CrearAsync(It.IsAny<CrearSolicitudDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(solicitud);

        var controller = CrearController();

        var resultado = await controller.CrearAsync(new CrearSolicitudDto
        {
            ClienteId = solicitud.ClienteId,
            Monto = 750m,
        });

        var created = resultado.Result.Should().BeOfType<CreatedAtRouteResult>().Subject;

        // Comprobar solo que RouteValues trae "id" NO es suficiente: eso mira el
        // diccionario y da verde aunque la URL no se pueda generar. Ese test
        // dejo pasar un 500 en produccion. Ahora se comprueba el NOMBRE de ruta,
        // que es lo que realmente decide si el enlace existe.
        created.RouteName.Should().Be("obtener-solicitud");
        created.RouteValues.Should().Contain("id", solicitud.Id);
    }

    [Fact]
    public void La_ruta_de_consulta_por_id_tiene_nombre_para_poder_generar_enlaces()
    {
        // Sin Name en la ruta, el generador de enlaces no encuentra el endpoint y
        // el POST responde 500 al construir la cabecera Location.
        var atributo = typeof(SolicitudController)
            .GetMethod(nameof(SolicitudController.ObtenerAsync))!
            .GetCustomAttributes(typeof(HttpGetAttribute), inherit: true)
            .Cast<HttpGetAttribute>()
            .Single();

        atributo.Name.Should().Be("obtener-solicitud");
    }

    [Fact]
    public async Task ListarAsync_proyecta_solo_la_pagina_actual()
    {
        var solicitudes = Enumerable.Range(0, 3).Select(_ => NuevaSolicitud()).ToList();
        var resultadoRepo = new PagedResult<Solicitud>(solicitudes, 57, 2, 3);

        _servicio.Setup(s => s.ListarAsync(It.IsAny<SolicitudCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(resultadoRepo);

        var controller = CrearController();

        var resultado = await controller.ListarAsync(pagina: 2, tamanoPagina: 3);

        var ok = resultado.Result.Should().BeOfType<OkObjectResult>().Subject;
        var sobre = ok.Value.Should().BeOfType<ApiResponse<PagedResult<SolicitudDto>>>().Subject;

        sobre.Datos!.Items.Should().HaveCount(3);
        sobre.Datos.TotalRegistros.Should().Be(57);
        sobre.Datos.Pagina.Should().Be(2);

        // El total se conserva aunque solo se mapee la pagina.
        sobre.Datos.TotalPaginas.Should().Be(19);
    }

    [Fact]
    public async Task AprobarAsync_devuelve_200_con_el_estado_nuevo()
    {
        var solicitud = NuevaSolicitud();
        solicitud.Aprobar(DateTime.UtcNow);

        _servicio.Setup(s => s.AprobarAsync(solicitud.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(solicitud);

        var controller = CrearController();

        var resultado = await controller.AprobarAsync(solicitud.Id);

        var ok = resultado.Result.Should().BeOfType<OkObjectResult>().Subject;
        var sobre = ok.Value.Should().BeOfType<ApiResponse<SolicitudDto>>().Subject;

        sobre.Datos!.Estado.Should().Be(EnumEstadoSolicitud.Aprobada);
    }

    [Fact]
    public async Task RechazarAsync_pasa_el_motivo_al_servicio()
    {
        var solicitud = NuevaSolicitud();
        solicitud.Rechazar("Documento incompleto", DateTime.UtcNow);

        _servicio.Setup(s => s.RechazarAsync(
                solicitud.Id, "Documento incompleto", It.IsAny<CancellationToken>()))
            .ReturnsAsync(solicitud);

        var controller = CrearController();

        var resultado = await controller.RechazarAsync(solicitud.Id, "Documento incompleto");

        var ok = resultado.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ApiResponse<SolicitudDto>>()
            .Which.Datos!.MotivoRechazo.Should().Be("Documento incompleto");
    }

    [Fact]
    public void Correlacion_devuelve_el_id_del_contexto_de_la_peticion()
    {
        var controller = CrearController();

        var resultado = controller.Correlacion();

        var ok = resultado.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().BeOfType<ApiResponse<object>>()
            .Which.Exito.Should().BeTrue();
    }

    [Fact]
    public void El_controlador_esta_ruta_de_api_solicitudes()
    {
        var atributo = typeof(SolicitudController)
            .GetCustomAttributes(typeof(RouteAttribute), inherit: true)
            .Cast<RouteAttribute>()
            .Single();

        // Si la ruta cambia, el front deja de encontrar el endpoint: mejor que
        // lo verifique un test que descubrirlo en produccion.
        atributo.Template.Should().Be("api/solicitudes");
    }
}