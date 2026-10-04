namespace Transacciones.CrossCutting.Exceptions;

/// <summary>
/// Error de negocio previsto: datos invalidos o una transicion de estado no permitida.
/// Se traduce a HTTP 400. No es un fallo del sistema, asi que no se registra
/// como error ni dispara alertas.
/// </summary>
public class BusinessRuleException : Exception
{
    /// <summary>Codigo estable de la regla incumplida, util para el front.</summary>
    public string Codigo { get; }

    public BusinessRuleException(string codigo, string mensaje)
        : base(mensaje)
    {
        Codigo = codigo;
    }

    public BusinessRuleException(string codigo, string mensaje, Exception inner)
        : base(mensaje, inner)
    {
        Codigo = codigo;
    }
}