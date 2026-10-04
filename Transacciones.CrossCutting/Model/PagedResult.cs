namespace Transacciones.CrossCutting.Model;

/// <summary>
/// Sobre de pagina estandar. Todas las listas de la API devuelven esta forma,
/// para que el front tenga un unico caso que resolver.
/// </summary>
/// <typeparam name="T">Tipo de los elementos.</typeparam>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalRegistros,
    int Pagina,
    int TamanoPagina)
{
    /// <summary>Total de paginas. Con tamano cero, cero.</summary>
    public int TotalPaginas => TamanoPagina <= 0
        ? 0
        : (int)Math.Ceiling(TotalRegistros / (double)TamanoPagina);

    /// <summary>Construye una pagina vacia.</summary>
    public static PagedResult<T> Vacia(int pagina, int tamanoPagina) =>
        new(Array.Empty<T>(), 0, pagina, tamanoPagina);
}