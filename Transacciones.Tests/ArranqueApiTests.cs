using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Transacciones.Tests;

/// <summary>
/// Arranca la API de verdad, con su <c>Program.cs</c>, su contenedor de DI, MVC,
/// Swagger, Quartz y health checks.
/// <para>
/// Es el unico tipo de prueba que detecta un registro de DI equivocado: los
/// unitarios con Moq no, y los de arranque de host tampoco lo inspeccionan.
/// </para>
/// <para>
/// Habla contra la base real. Si <c>TransaccionesDb</c> no existe, la prueba de
/// listado falla, y eso es correcto: la API tambien fallaria.
/// </para>
/// </summary>
public sealed class ArranqueApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _cliente;

    public ArranqueApiTests(WebApplicationFactory<Program> fabrica)
    {
        _cliente = fabrica.CreateClient();
    }

    [Fact]
    public async Task El_health_check_responde_sin_error()
    {
        var respuesta = await _cliente.GetAsync("/api/healthcheck");

        // El check de la base: si TransaccionesDb no esta, falla. Es lo que
        // distingue un health check real de uno que siempre dice "bien".
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Swagger_esta_disponible()
    {
        var respuesta = await _cliente.GetAsync("/swagger/v1/swagger.json");

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        var contenido = await respuesta.Content.ReadAsStringAsync();
        contenido.Should().Contain("/api/solicitudes");
    }

    [Fact]
    public async Task Listar_solicitudes_devuelve_200_con_el_sobre_esperado()
    {
        var respuesta = await _cliente.GetAsync("/api/solicitudes?pagina=1&tamanoPagina=5");

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
        respuesta.Content.Headers.ContentType!.MediaType.Should().Be("application/json");

        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());

        var raiz = json.RootElement;
        raiz.GetProperty("exito").GetBoolean().Should().BeTrue();

        var datos = raiz.GetProperty("datos");
        datos.GetProperty("totalRegistros").GetInt32().Should().BeGreaterThanOrEqualTo(0);
        datos.GetProperty("items").GetArrayLength().Should().BeLessThanOrEqualTo(5);
    }

    [Fact]
    public async Task La_respuesta_lleva_cabecera_de_correlacion()
    {
        var respuesta = await _cliente.GetAsync("/api/solicitudes");

        // El middleware la genera si no viene, para que el usuario pueda
        // reportar la incidencia y se pueda rastrear en los logs.
        respuesta.Headers.Should().ContainKey("X_correlationId");
        respuesta.Headers.GetValues("X_correlationId").Should().NotBeEmpty();
    }

    [Fact]
    public async Task La_correlacion_entrada_se_propaga_a_la_respuesta()
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, "/api/solicitudes");
        peticion.Headers.Add("X_correlationId", "prueba-arranque-001");

        var respuesta = await _cliente.SendAsync(peticion);

        respuesta.Headers.GetValues("X_correlationId").Single()
            .Should().Be("prueba-arranque-001");
    }

    [Fact]
    public async Task Peticion_con_multi_risk_atiende_contra_la_base_SME()
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, "/api/solicitudes");
        peticion.Headers.Add("X_isMultiRisk", "true");

        var respuesta = await _cliente.SendAsync(peticion);

        // La cabecera no debe producir un error: si el enrutado a la base SME
        // estuviera roto (por ejemplo, por una connection string vacia), aqui
        // saldria un 500 en vez de un 200.
        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Obtener_una_solicitud_inexistente_devuelve_404_con_codigo_claro()
    {
        var id = Guid.NewGuid();
        var respuesta = await _cliente.GetAsync($"/api/solicitudes/{id}");

        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());

        // El ExceptionMiddleware traduce el error: 404 y no un 500 opaco.
        json.RootElement.GetProperty("exito").GetBoolean().Should().BeFalse();
        json.RootElement.GetProperty("codigo").GetString().Should().Be("GEN-404");
    }

    [Fact]
    public async Task Crear_sin_cuerpo_devuelve_400_y_no_500()
    {
        using var contenido = new StringContent(
            "{}",
            System.Text.Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));

        var respuesta = await _cliente.PostAsync("/api/solicitudes", contenido);

        // Modelo invalido es 400. Un 500 aqui significaria que la validacion no
        // esta conectada al pipeline.
        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Crear_solicitud_devuelve_201_con_cabecera_Location()
    {
        // Regresion: el POST guardaba la solicitud y devolvia 500 al construir la
        // cabecera Location, porque la ruta por id no tenia nombre. Un fallo que
        // deja datos guardados y una respuesta de error al usuario.
        var cuerpo = new StringContent(
            $$"""
              {
                "clienteId": "{{Guid.NewGuid()}}",
                "monto": 1500.00,
                "moneda": "PEN",
                "vigenciaDias": 30,
                "observacion": "prueba de arranque"
              }
              """,
            System.Text.Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));

        var respuesta = await _cliente.PostAsync("/api/solicitudes", cuerpo);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Created);

        // La cabecera Location debe existir Y ser utilizable: seguirla tiene que
        // devolver 200. Es la unica comprobacion que de verdad verifica que la
        // generacion de enlaces funciona.
        respuesta.Headers.Location.Should().NotBeNull();

        var siguiendo = await _cliente.GetAsync(respuesta.Headers.Location);
        siguiendo.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task El_endpoint_de_correlacion_devuelve_el_id_de_la_peticion()
    {
        var respuesta = await _cliente.GetAsync("/api/solicitudes/correlacion");

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK);

        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var id = json.RootElement.GetProperty("datos").GetProperty("correlacionId").GetString();

        id.Should().NotBeNullOrWhiteSpace();
    }

    // =======================================================================
    // Transiciones de estado
    // -----------------------------------------------------------------------
    // Estas pruebas cubren lo que hasta ahora no cubria nada: el POST tenia
    // tests, aprobar, rechazar y anular no. Un fallo ahi era invisible para
    // `dotnet test` y solo aparecia a mano en el navegador, donde el
    // ExceptionMiddleware esconde la excepcion y solo deja un "GEN-500" sin
    // detalle. Un fallo de escritura en base de datos, que es el caso mas grave,
    // salia como un error generico indistinguible de cualquier otro.
    // =======================================================================

    [Fact]
    public async Task Aprobar_una_solicitud_registrada_devuelve_200_y_pasa_a_Aprobada()
    {
        var id = await CrearSolicitudAsync();

        using var peticion = new HttpRequestMessage(HttpMethod.Put, $"/api/solicitudes/{id}/aprobar");

        var respuesta = await _cliente.SendAsync(peticion);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await MotivoAsync(respuesta));

        (await EstadoAsync(respuesta)).Should().Be(2, "aprobar deja la solicitud en Aprobada");
    }

    [Fact]
    public async Task Aprobar_devuelve_la_solicitud_con_los_campos_de_la_aprobacion()
    {
        var id = await CrearSolicitudAsync();

        using var peticion = new HttpRequestMessage(HttpMethod.Put, $"/api/solicitudes/{id}/aprobar");
        var respuesta = await _cliente.SendAsync(peticion);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await MotivoAsync(respuesta));

        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        var datos = json.RootElement.GetProperty("datos");

        datos.GetProperty("fechaAprobacion").ValueKind.Should().NotBe(
            JsonValueKind.Null,
            "AprobarAsync estampa FechaAprobacion; si llega nula, el campo no se esta guardando");
    }

    [Fact]
    public async Task Rechazar_devuelve_200_y_guarda_el_motivo()
    {
        var id = await CrearSolicitudAsync();

        using var peticion = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/solicitudes/{id}/rechazar?motivo=Documentacion%20incompleta");

        var respuesta = await _cliente.SendAsync(peticion);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await MotivoAsync(respuesta));

        (await EstadoAsync(respuesta)).Should().Be(3);

        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("datos").GetProperty("motivoRechazo").GetString()
            .Should().Be("Documentacion incompleta", "el motivo se guarda tal cual se mando");
    }

    [Fact]
    public async Task Anular_devuelve_200_y_pasa_a_Anulada()
    {
        var id = await CrearSolicitudAsync();

        using var peticion = new HttpRequestMessage(HttpMethod.Put, $"/api/solicitudes/{id}/anular");

        var respuesta = await _cliente.SendAsync(peticion);

        respuesta.StatusCode.Should().Be(HttpStatusCode.OK, await MotivoAsync(respuesta));

        (await EstadoAsync(respuesta)).Should().Be(5, "Anulada es el valor 5 del enum");
    }

    [Fact]
    public async Task Anular_una_solicitud_ya_aprobada_sigue_siendo_valido()
    {
        // Es la unica transicion que se permite despues de Aprobada. Si esta
        // prueba falla, se ha perdido una regla del dominio.
        var id = await CrearSolicitudAsync();

        using var aprobar = new HttpRequestMessage(HttpMethod.Put, $"/api/solicitudes/{id}/aprobar");
        var trasAprobar = await _cliente.SendAsync(aprobar);
        trasAprobar.StatusCode.Should().Be(HttpStatusCode.OK, await MotivoAsync(trasAprobar));

        using var anular = new HttpRequestMessage(HttpMethod.Put, $"/api/solicitudes/{id}/anular");
        var trasAnular = await _cliente.SendAsync(anular);

        trasAnular.StatusCode.Should().Be(HttpStatusCode.OK, await MotivoAsync(trasAnular));

        (await EstadoAsync(trasAnular)).Should().Be(5);
    }

    [Fact]
    public async Task Aprobar_una_solicitud_ya_anulada_devuelve_400_y_no_500()
    {
        // La contraparte mas importante de este bloque. Si una transicion
        // invalida devolviera 500 en vez de 400, significaria que el
        // ExceptionMiddleware no esta traduciendo BusinessRuleException, y
        // cualquier error de negocio se presentaria como fallo del sistema.
        var id = await CrearSolicitudAsync();

        using var anular = new HttpRequestMessage(HttpMethod.Put, $"/api/solicitudes/{id}/anular");
        await _cliente.SendAsync(anular);

        using var aprobar = new HttpRequestMessage(HttpMethod.Put, $"/api/solicitudes/{id}/aprobar");
        var respuesta = await _cliente.SendAsync(aprobar);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await MotivoAsync(respuesta));

        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("codigo").GetString().Should().Be("SOL-003");
    }

    [Fact]
    public async Task Rechazar_sin_motivo_devuelve_400_con_SOL_004()
    {
        var id = await CrearSolicitudAsync();

        using var peticion = new HttpRequestMessage(HttpMethod.Put, $"/api/solicitudes/{id}/rechazar");
        var respuesta = await _cliente.SendAsync(peticion);

        respuesta.StatusCode.Should().Be(HttpStatusCode.BadRequest, await MotivoAsync(respuesta));

        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("codigo").GetString().Should().Be("SOL-004");
    }

    [Fact]
    public async Task Aprobar_una_solicitud_inexistente_devuelve_404_y_no_500()
    {
        using var peticion = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/solicitudes/{Guid.NewGuid()}/aprobar");

        var respuesta = await _cliente.SendAsync(peticion);

        respuesta.StatusCode.Should().Be(HttpStatusCode.NotFound, await MotivoAsync(respuesta));
    }

    // -----------------------------------------------------------------------
    // Utilidades
    // -----------------------------------------------------------------------

    /// <summary>Crea una solicitud y devuelve su id, leido de la cabecera Location.</summary>
    private async Task<Guid> CrearSolicitudAsync()
    {
        var cuerpo = new StringContent(
            $$"""
              {
                "clienteId": "{{Guid.NewGuid()}}",
                "monto": 2500.00,
                "moneda": "PEN",
                "vigenciaDias": 30,
                "observacion": "prueba de transicion"
              }
              """,
            System.Text.Encoding.UTF8,
            new MediaTypeHeaderValue("application/json"));

        var respuesta = await _cliente.PostAsync("/api/solicitudes", cuerpo);

        respuesta.StatusCode.Should().Be(HttpStatusCode.Created, await MotivoAsync(respuesta));

        // El id se saca de la cabecera Location y no de "datos.id": asi la prueba
        // no depende de la forma del DTO, solo de que la ruta nombrada resuelva.
        var location = respuesta.Headers.Location!.ToString();

        return Guid.Parse(location[(location.LastIndexOf('/') + 1)..]);
    }

    /// <summary>
    /// Texto para el mensaje de asercion.
    /// <para>
    /// El <c>ExceptionMiddleware</c> responde un "GEN-500" generico a proposito,
    /// para no filtrar nombres de servidor ni rutas de archivo. Eso esta bien en
    /// produccion, pero deja a quien depura sin saber que paso. Poner el cuerpo
    /// real de la respuesta en el mensaje del fallo hace que el test diga
    /// exactamente lo mismo que veria el navegador, y bastante mas.
    /// </para>
    /// </summary>
    private static async Task<string> MotivoAsync(HttpResponseMessage respuesta)
    {
        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        return $"la API respondio {(int)respuesta.StatusCode} con: {cuerpo}";
    }

    /// <summary>Lee <c>datos.estado</c> de la respuesta.</summary>
    private static async Task<int> EstadoAsync(HttpResponseMessage respuesta)
    {
        using var json = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());

        return json.RootElement.GetProperty("datos").GetProperty("estado").GetInt32();
    }
}