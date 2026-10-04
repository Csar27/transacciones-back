using AutoMapper;
using Transacciones.Core.Models;
using Transacciones.Core.Services;

namespace Transacciones.WebApi.Mapping;

/// <summary>
/// Perfil de mapeo de Solicitud.
/// <para>
/// Va en WebApi, no en Core ni en Services. La capa de dominio no debe saber
/// que existe AutoMapper: si el mapeo viviera ahi, la entidad dejaria de ser
/// un POCO y las pruebas del servicio necesitarian el contenedor de DI.
/// </para>
/// </summary>
public sealed class SolicitudProfile : Profile
{
    public SolicitudProfile()
    {
        // Entidad -> DTO. El estado se pasa tambien como texto para que el front
        // no tenga que traducir el enum.
        CreateMap<Solicitud, SolicitudDto>()
            .ForMember(d => d.EstadoDescripcion, opt => opt.MapFrom(s => s.Estado.ToString()));

        // Inverso. Solo se mapean las propiedades con setter: las de solo lectura
        // (EsTerminal) y las metodos (EstaVencida) las calcula la entidad y
        // AutoMapper las deja fuera por defecto. Forzar el Ignore sobre un
        // miembro estatico, ademas, no compila.
        CreateMap<SolicitudDto, Solicitud>();
    }
}