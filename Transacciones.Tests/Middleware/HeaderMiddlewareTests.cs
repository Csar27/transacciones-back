using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Transacciones.CrossCutting.Constant;
using Transacciones.CrossCutting.Model;
using Transacciones.WebApi.Middleware;
using Xunit;

namespace Transacciones.Tests.Middleware;

/// <summary>
/// Pruebas del enrutado por cabecera.
/// <para>
/// Es la pieza mas facil de romper sin avisar: si el middleware deja de leer
/// <c>X_isMultiRisk</c>, la API sigue respondiendo 200, pero escribe contra la
/// base equivocada. Un test de integracion no lo detectaria sin dos bases.
/// </para>
/// </summary>
public sealed class HeaderMiddlewareTests
{
    private static async Task<(CurrentRequestContext Contexto, HttpResponse Respuesta)> EjecutarAsync(
        IDictionary<string, string>? cabeceras = null,
        CancellationToken ct = default)
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        if (cabeceras is not null)
        {
            foreach (var (clave, valor) in cabeceras)
            {
                http.Request.Headers[clave] = valor;
            }
        }

        var contexto = new CurrentRequestContext();

        var middleware = new HeaderMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(http, contexto);

        return (contexto, http.Response);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("True")]
    [InlineData("TRUE")]
    [InlineData("1")]
    public async Task Cabecera_afirmativa_enruta_a_multi_risk(string valor)
    {
        var (contexto, _) = await EjecutarAsync(new Dictionary<string, string>
        {
            [HeaderNames.MultiRisk] = valor,
        });

        contexto.IsMultiRisk.Should().BeTrue();
    }

    [Theory]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("no")]
    [InlineData("quizá")]
    public async Task Valor_no_reconocido_va_a_la_base_normal(string valor)
    {
        var (contexto, _) = await EjecutarAsync(new Dictionary<string, string>
        {
            [HeaderNames.MultiRisk] = valor,
        });

        contexto.IsMultiRisk.Should().BeFalse();
    }

    [Fact]
    public async Task Sin_cabecera_la_peticion_va_a_la_base_normal()
    {
        var (contexto, _) = await EjecutarAsync();

        contexto.IsMultiRisk.Should().BeFalse();
    }

    [Fact]
    public async Task Se_genera_correlacion_cuando_no_viene_informada()
    {
        var (contexto, respuesta) = await EjecutarAsync();

        contexto.CorrelationId.Should().NotBeNullOrWhiteSpace();

        // Se devuelve en la respuesta para que el front pueda citarla al
        // reportar una incidencia.
        respuesta.Headers[HeaderNames.CorrelationId].ToString()
            .Should().Be(contexto.CorrelationId);
    }

    [Fact]
    public async Task Se_reutiliza_la_correlacion_que_viene_del_front()
    {
        var (contexto, respuesta) = await EjecutarAsync(new Dictionary<string, string>
        {
            [HeaderNames.CorrelationId] = "abc-123-def",
        });

        contexto.CorrelationId.Should().Be("abc-123-def");
        respuesta.Headers[HeaderNames.CorrelationId].ToString().Should().Be("abc-123-def");
    }

    [Fact]
    public async Task El_usuario_se_toma_de_la_cabecera_cuando_viene()
    {
        var (contexto, _) = await EjecutarAsync(new Dictionary<string, string>
        {
            [HeaderNames.UserId] = "operador.01",
        });

        contexto.UserId.Should().Be("operador.01");
    }

    [Fact]
    public async Task El_usuario_queda_null_si_no_viene()
    {
        var (contexto, _) = await EjecutarAsync();

        contexto.UserId.Should().BeNull();
    }

    [Fact]
    public async Task El_middleware_sigue_y_pasa_al_siguiente_del_pipeline()
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        var avanzado = false;
        var middleware = new HeaderMiddleware(_ =>
        {
            avanzado = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(http, new CurrentRequestContext());

        avanzado.Should().BeTrue();
    }
}