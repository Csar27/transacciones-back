using Dapper;
using Microsoft.Data.SqlClient;

namespace Transacciones.Data.Repositories;

/// <summary>
/// Ejecuta SQL parametrizado por Dapper.
/// <para>
/// Existe para lo que EF hace mal: consultas de solo lectura que conviene hacer
/// crudas porque castear a entidad cuesta mas que el SQL. Todo con parametros,
/// nunca concatenar.
/// </para>
/// </summary>
public sealed class SqlExecutor(ISqlConnectionFactory conexiones)
{
    /// <summary>Abre conexion y ejecuta un query de lectura.</summary>
    public async Task<IEnumerable<T>> ConsultarAsync<T>(
        string sql,
        object? parametros = null,
        CancellationToken ct = default)
    {
        await using var conexion = conexiones.Crear();

        return await conexion.QueryAsync<T>(
                new CommandDefinition(sql, parametros, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    /// <summary>Abre conexion y ejecuta un escalar (un solo valor).</summary>
    public async Task<T?> EscalarAsync<T>(
        string sql,
        object? parametros = null,
        CancellationToken ct = default)
    {
        await using var conexion = conexiones.Crear();

        return await conexion.ExecuteScalarAsync<T>(
                new CommandDefinition(sql, parametros, cancellationToken: ct))
            .ConfigureAwait(false);
    }

    /// <summary>Abre conexion y ejecuta un comando que no devuelve filas.</summary>
    public async Task<int> EjecutarAsync(
        string sql,
        object? parametros = null,
        CancellationToken ct = default)
    {
        await using var conexion = conexiones.Crear();

        return await conexion.ExecuteAsync(
                new CommandDefinition(sql, parametros, cancellationToken: ct))
            .ConfigureAwait(false);
    }
}