using Npgsql;

namespace Maran.Host.IntegrationTests.Fixtures;

/// <summary>
/// Hunts a value through EVERY text, JSON and byte column of every user table in a database, so a
/// claim that a secret "is stored nowhere" is checked against the whole database rather than against
/// the one table a reviewer thought of.
/// </summary>
/// <remarks>
/// <para>
/// <b>The probe owes the proof that it can see.</b> An empty answer is the same whether the value is
/// absent or the search could not match, so a test using this plants the value in a table of its own
/// first and requires the probe to find exactly that (the positive control, rules/testing.md). Byte
/// columns are searched as bytes, not as text: a Wolverine envelope body is <c>bytea</c>, which a
/// <c>::text</c> cast renders as hex and would never match a plaintext — the false pass this
/// repository has already had once.
/// </para>
/// <para>
/// The search is <c>strpos</c>/<c>position</c>, not <c>LIKE</c>: a URL-safe token contains <c>_</c>,
/// which <c>LIKE</c> reads as a wildcard.
/// </para>
/// </remarks>
public static class PlaintextProbe
{
    /// <summary>The column types that can hold readable text, as <c>information_schema</c> names them.</summary>
    private static readonly string[] TextTypes = ["text", "character varying", "character", "json", "jsonb"];

    /// <summary>Finds where <paramref name="needle"/> appears in the database.</summary>
    /// <param name="connectionString">The database to search.</param>
    /// <param name="needle">The plaintext to hunt.</param>
    /// <returns>Each hit as <c>schema.table.column</c>; empty when the value is nowhere.</returns>
    public static async Task<IReadOnlyList<string>> FindAsync(string connectionString, string needle)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var columns = new List<(string Schema, string Table, string Column, string Type)>();
        await using (var list = new NpgsqlCommand(
            """
            select c.table_schema, c.table_name, c.column_name, c.data_type
            from information_schema.columns c
            join information_schema.tables t
              on t.table_schema = c.table_schema and t.table_name = c.table_name and t.table_type = 'BASE TABLE'
            where c.table_schema not in ('pg_catalog', 'information_schema')
              and (c.data_type = 'bytea' or c.data_type = any(@textTypes))
            """,
            connection))
        {
            list.Parameters.AddWithValue("textTypes", TextTypes);
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        var hits = new List<string>();
        foreach (var (schema, table, column, type) in columns)
        {
            // raw-sql: identifiers come from information_schema (never from a caller) and are quoted;
            // the needle is a parameter.
            var predicate = type == "bytea"
                ? $"position(convert_to(@needle, 'UTF8') in \"{column}\") > 0"
                : $"strpos(\"{column}\"::text, @needle) > 0";
            await using var probe = new NpgsqlCommand(
                $"select exists (select 1 from \"{schema}\".\"{table}\" where {predicate})", connection);
            probe.Parameters.AddWithValue("needle", needle);
            if ((bool)(await probe.ExecuteScalarAsync())!)
            {
                hits.Add($"{schema}.{table}.{column}");
            }
        }

        return hits;
    }

    /// <summary>
    /// Plants <paramref name="needle"/> in a throwaway table, one text column and one byte column, so a
    /// test can require <see cref="FindAsync"/> to find it before trusting its silence elsewhere.
    /// </summary>
    /// <param name="connectionString">The database to plant it in.</param>
    /// <param name="needle">The plaintext to plant.</param>
    public static async Task PlantControlAsync(string connectionString, string needle)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        await using var create = new NpgsqlCommand(
            "create table public.\"ProbeControl\" (\"AsText\" text, \"AsBytes\" bytea)", connection);
        await create.ExecuteNonQueryAsync();

        await using var insert = new NpgsqlCommand(
            "insert into public.\"ProbeControl\" values (@needle, convert_to(@needle, 'UTF8'))", connection);
        insert.Parameters.AddWithValue("needle", needle);
        await insert.ExecuteNonQueryAsync();
    }
}
