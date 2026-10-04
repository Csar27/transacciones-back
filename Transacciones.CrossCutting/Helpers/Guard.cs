using System.Diagnostics.CodeAnalysis;

namespace Transacciones.CrossCutting.Helpers;

/// <summary>
/// Guarda contra nulls en la entrada de datos.
/// Funciona por argumento, no por flujo de control: si la condicion se cumple,
/// la peticion sigue; si no, lanza.
/// </summary>
public static class Guard
{
    /// <summary>Lanza <see cref="ArgumentNullException"/> si el valor es null.</summary>
    public static T NotNull<T>(
        [NotNull] T? valor,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(valor))] string? nombre = null)
    {
        if (valor is null)
        {
            throw new ArgumentNullException(nombre);
        }

        return valor;
    }

    /// <summary>Lanza <see cref="ArgumentException"/> si la cadena es null, vacia o solo espacios.</summary>
    public static string NotBlank(
        [NotNull] string? valor,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(valor))] string? nombre = null)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            throw new ArgumentException("El valor no puede ser nulo ni estar vacio.", nombre);
        }

        return valor;
    }
}