namespace Transacciones.CrossCutting.Enums;

/// <summary>
/// Estados posibles de una solicitud.
/// El valor numerico es parte del contrato: se persiste en base de datos,
/// asi que reordenar o reutilizar un valor rompe datos existentes.
/// </summary>
public enum EnumEstadoSolicitud
{
    /// <summary>Creada pero todavia no enviada.</summary>
    Borrador = 0,

    /// <summary>Registrada y pendiente de validacion.</summary>
    Registrada = 1,

    /// <summary>Aprobada por el flujo correspondiente.</summary>
    Aprobada = 2,

    /// <summary>Rechazada. Estado terminal.</summary>
    Rechazada = 3,

    /// <summary>No atendida antes de su fecha de vencimiento. Estado terminal.</summary>
    Vencida = 4,

    /// <summary>Anulada por el usuario. Estado terminal.</summary>
    Anulada = 5,
}