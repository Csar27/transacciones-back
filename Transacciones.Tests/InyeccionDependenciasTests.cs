using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Transacciones.Core.Repositories;
using Transacciones.Core.Services;
using Transacciones.CrossCutting.Model;
using Transacciones.WebApi.Extension;
using Xunit;

namespace Transacciones.Tests;

/// <summary>
/// Comprueba los registros de DI de la aplicacion.
/// <para>
/// Las pruebas unitarias con Moq no detectan un servicio que nunca se registro:
/// todas las dependencias estan simuladas y el problema no aparece. Aqui se
/// usan las extensiones REALES de registro, sin simular nada.
/// </para>
/// <para>
/// No se incluyen <c>AddControllers</c>, <c>AddSwagger</c> ni Quartz: dependen
/// de <c>IWebHostEnvironment</c> e <c>IHostApplicationLifetime</c>, que solo
/// existen dentro de un host. Esos registros se comprueban arrancando la API de
/// verdad, en <c>ArranqueApiTests</c>.
/// </para>
/// </summary>
public sealed class InyeccionDependenciasTests
{
    private static ServiceProvider ConstruirProveedor()
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Las DOS connection strings: si falta una, ValidateOnStart falla al
                // construir el proveedor. Es deliberado.
                ["ABCMultiSettings:ConnectionStringAff"] =
                    "Server=(local)\\SQLEXPRESS;Database=TransaccionesDb;Trusted_Connection=True;TrustServerCertificate=True",
                ["ABCMultiSettings:ConnectionStringSME"] =
                    "Server=(local)\\SQLEXPRESS;Database=TransaccionesDbSme;Trusted_Connection=True;TrustServerCertificate=True",

                ["ExternalApis:NotificacionesUrl"] = "http://localhost:5201/",
                ["ExternalApis:CatalogoUrl"] = "http://localhost:5202/",

                ["AutoMapper:LicenseKey"] = string.Empty,
                ["Cors:Origenes:0"] = "http://localhost:4200",
            })
            .Build();

        var servicios = new ServiceCollection();

        servicios.AddLogging();

        // En un host real, IConfiguration ya esta registrada. Aqui hay que
        // hacerlo a mano porque este contenedor no lo es.
        servicios.AddSingleton<IConfiguration>(configuracion);

        // Las mismas extensiones que llama Program.cs.
        servicios.AddCrossCuttingReferences();
        servicios.AddDataReferences(configuracion);
        servicios.AddApiReferences(configuracion);
        servicios.AddServices();
        servicios.RegisterAutoMapper(configuracion);
        servicios.AddCORS(configuracion);

        return servicios.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Fact]
    public void El_proveedor_se_construye_sin_errores_de_resolucion()
    {
        using var proveedor = ConstruirProveedor();

        // Si algo no se puede construir, ValidateOnBuild ya ha fallado al crear el
        // proveedor. Esta asercion documenta la intencion del test.
        proveedor.Should().NotBeNull();
    }

    [Fact]
    public void ISolicitudService_se_resuelve()
    {
        using var proveedor = ConstruirProveedor();

        // Se abre un ambito: los servicios de ambito no se pueden pedir en la raiz,
        // y pedirlos ahi daria un falso positivo de "todo bien".
        using var ambito = proveedor.CreateScope();

        ambito.ServiceProvider.GetRequiredService<ISolicitudService>()
            .Should().NotBeNull();
    }

    [Fact]
    public void INotificacionService_se_resuelve()
    {
        using var proveedor = ConstruirProveedor();
        using var ambito = proveedor.CreateScope();

        // Esta es la que faltaba: NotificacionService se registraba solo contra su
        // clase concreta, no contra su interfaz. El error salia al construir el
        // contenedor, antes de atender ninguna peticion.
        ambito.ServiceProvider.GetRequiredService<INotificacionService>()
            .Should().NotBeNull();
    }

    [Fact]
    public void ISolicitudRepository_se_resuelve()
    {
        using var proveedor = ConstruirProveedor();
        using var ambito = proveedor.CreateScope();

        ambito.ServiceProvider.GetRequiredService<ISolicitudRepository>()
            .Should().NotBeNull();
    }

    [Fact]
    public void ICurrentRequestContext_se_resuelve_y_es_scoped()
    {
        using var proveedor = ConstruirProveedor();

        using var ambitoA = proveedor.CreateScope();
        using var ambitoB = proveedor.CreateScope();

        var contextoA = ambitoA.ServiceProvider.GetRequiredService<ICurrentRequestContext>();
        var contextoB = ambitoB.ServiceProvider.GetRequiredService<ICurrentRequestContext>();

        // Scoped y no singleton: un singleton mezclaria el enrutado X_isMultiRisk
        // entre peticiones simultaneas.
        contextoA.Should().NotBeSameAs(contextoB);
    }

    [Fact]
    public void IMapper_se_resuelve_con_el_perfil_de_solicitud()
    {
        using var proveedor = ConstruirProveedor();
        using var ambito = proveedor.CreateScope();

        var mapper = ambito.ServiceProvider.GetRequiredService<AutoMapper.IMapper>();

        var entidad = new Transacciones.Core.Models.Solicitud
        {
            Id = Guid.NewGuid(),
            Codigo = "SOL-2026-000001",
            Estado = Transacciones.CrossCutting.Enums.EnumEstadoSolicitud.Registrada,
        };

        var dto = mapper.Map<Transacciones.Core.Models.SolicitudDto>(entidad);

        dto.Codigo.Should().Be("SOL-2026-000001");
        dto.EstadoDescripcion.Should().Be("Registrada");
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_servicio_da_la_misma_instancia_dentro_del_ambito()
    {
        using var proveedor = ConstruirProveedor();
        using var ambito = proveedor.CreateScope();

        var primero = ambito.ServiceProvider.GetRequiredService<ISolicitudService>();
        var segundo = ambito.ServiceProvider.GetRequiredService<ISolicitudService>();

        primero.Should().BeSameAs(segundo);
    }
}