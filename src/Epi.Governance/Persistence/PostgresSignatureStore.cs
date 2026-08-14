using Epi.ContentCore;
using Epi.Signature;
using Npgsql;

namespace Epi.Governance.Persistence;

/// <summary>
/// The durable signature store: an append-only table (ADR-020 decision 7, CAP-AUD-003).
/// </summary>
/// <remarks>
/// Append-only is enforced by the database, not by this class, on the same reasoning as the
/// audit sink: an application-level guarantee protects against the application, and a
/// database-level one protects against anything holding a connection. A signature that could be
/// amended after the fact would not be evidence of anything.
/// </remarks>
public sealed class PostgresSignatureStore(string connectionString)
    : ISignatureStore, IAsyncDisposable
{
    private readonly Lazy<NpgsqlDataSource> _source =
        new(() => new NpgsqlDataSourceBuilder(connectionString).Build());

    /// <summary>Creates the table and the trigger that makes it append-only.</summary>
    /// <remarks>
    /// Idempotent, so it is safe to run at start-up. In a qualified environment this belongs in
    /// a controlled migration (D3 Section 10.3) rather than at application start; it is here so
    /// the demonstration stands up unattended.
    /// </remarks>
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _source.Value.CreateCommand("""
            CREATE TABLE IF NOT EXISTS signature_manifest (
                reference            TEXT        PRIMARY KEY,
                signer_identifier    TEXT        NOT NULL,
                signer_printed_name  TEXT        NOT NULL,
                meaning              TEXT        NOT NULL,
                document_system      TEXT        NOT NULL,
                document_value       TEXT        NOT NULL,
                document_version     INTEGER     NOT NULL,
                content_hash         TEXT        NOT NULL,
                signed_at            TIMESTAMPTZ NOT NULL,
                reason               TEXT        NULL
            );

            CREATE OR REPLACE FUNCTION signature_manifest_is_append_only() RETURNS TRIGGER AS $$
            BEGIN
                RAISE EXCEPTION 'signature_manifest is append-only: % is not permitted', TG_OP
                    USING ERRCODE = 'restrict_violation';
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS signature_manifest_no_change ON signature_manifest;
            CREATE TRIGGER signature_manifest_no_change
                BEFORE UPDATE OR DELETE ON signature_manifest
                FOR EACH ROW EXECUTE FUNCTION signature_manifest_is_append_only();
            """);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AppendAsync(
        SignatureManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        // No ON CONFLICT clause, deliberately. A second signature under an existing reference
        // is an amendment however it is spelled, so the primary key refuses it.
        await using var command = _source.Value.CreateCommand("""
            INSERT INTO signature_manifest
                (reference, signer_identifier, signer_printed_name, meaning, document_system,
                 document_value, document_version, content_hash, signed_at, reason)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """);

        command.Parameters.AddWithValue(manifest.Reference);
        command.Parameters.AddWithValue(manifest.SignerIdentifier);
        command.Parameters.AddWithValue(manifest.SignerPrintedName);
        command.Parameters.AddWithValue(manifest.Meaning.ToString());
        command.Parameters.AddWithValue(manifest.Document.System);
        command.Parameters.AddWithValue(manifest.Document.Value);
        command.Parameters.AddWithValue(manifest.Version);
        command.Parameters.AddWithValue(manifest.ContentHash);
        command.Parameters.AddWithValue(manifest.SignedAt);
        command.Parameters.AddWithValue((object?)manifest.Reason ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SignatureManifest?> FindAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        await using var command = _source.Value.CreateCommand("""
            SELECT reference, signer_identifier, signer_printed_name, meaning, document_system,
                   document_value, document_version, content_hash, signed_at, reason
            FROM signature_manifest
            WHERE reference = $1
            """);

        command.Parameters.AddWithValue(reference);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SignatureManifest(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            Enum.Parse<SignatureMeaning>(reader.GetString(3)),
            new DocumentIdentity(reader.GetString(4), reader.GetString(5)),
            reader.GetInt32(6),
            reader.GetString(7),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : reader.GetString(9));
    }

    public async ValueTask DisposeAsync()
    {
        if (_source.IsValueCreated)
        {
            await _source.Value.DisposeAsync();
        }
    }
}
