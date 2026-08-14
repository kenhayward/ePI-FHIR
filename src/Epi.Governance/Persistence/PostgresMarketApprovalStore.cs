using Epi.Lifecycle;
using Npgsql;

namespace Epi.Governance.Persistence;

/// <summary>
/// The durable per-market approval store: an append-only transition table, keyed by version
/// and market (ADR-005, ADR-019 decision 2, CAP-LCM-003).
/// </summary>
/// <remarks>
/// A separate table from the internal lifecycle, not a column on it. One table with a nullable
/// market column would make "approved" ambiguous between internal and regulatory approval, which
/// is precisely the conflation ADR-005 exists to prevent.
/// </remarks>
public sealed class PostgresMarketApprovalStore(string connectionString)
    : IMarketApprovalStore, IAsyncDisposable
{
    private readonly Lazy<NpgsqlDataSource> _source =
        new(() => new NpgsqlDataSourceBuilder(connectionString).Build());

    /// <summary>Creates the table and the trigger that makes it append-only.</summary>
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _source.Value.CreateCommand("""
            CREATE TABLE IF NOT EXISTS market_approval_transition (
                id                  BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                document_identifier TEXT        NOT NULL,
                document_version    INTEGER     NOT NULL,
                market              TEXT        NOT NULL,
                from_state          TEXT        NOT NULL,
                to_state            TEXT        NOT NULL,
                action              TEXT        NOT NULL,
                actor               TEXT        NOT NULL,
                occurred_at         TIMESTAMPTZ NOT NULL,
                reason              TEXT        NULL,
                signature_reference TEXT        NULL
            );

            CREATE INDEX IF NOT EXISTS market_approval_by_subject
                ON market_approval_transition (document_identifier, document_version, market, id);

            CREATE INDEX IF NOT EXISTS market_approval_by_signature
                ON market_approval_transition (signature_reference)
                WHERE signature_reference IS NOT NULL;

            CREATE OR REPLACE FUNCTION market_approval_is_append_only() RETURNS TRIGGER AS $$
            BEGIN
                RAISE EXCEPTION 'market_approval_transition is append-only: % is not permitted',
                    TG_OP USING ERRCODE = 'restrict_violation';
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS market_approval_no_change ON market_approval_transition;
            CREATE TRIGGER market_approval_no_change
                BEFORE UPDATE OR DELETE ON market_approval_transition
                FOR EACH ROW EXECUTE FUNCTION market_approval_is_append_only();
            """);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<string?> CurrentStateAsync(
        MarketVersion subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        // Null when the version has never moved in this market. The service supplies the
        // model's initial state, so nothing is written to say a version is unsubmitted.
        await using var command = _source.Value.CreateCommand("""
            SELECT to_state FROM market_approval_transition
            WHERE document_identifier = $1 AND document_version = $2 AND market = $3
            ORDER BY id DESC LIMIT 1
            """);

        command.Parameters.AddWithValue(subject.Version.DocumentIdentifier);
        command.Parameters.AddWithValue(subject.Version.Version);
        command.Parameters.AddWithValue(subject.Market);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<IReadOnlyList<MarketStateTransition>> HistoryAsync(
        MarketVersion subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        await using var command = _source.Value.CreateCommand("""
            SELECT from_state, to_state, action, actor, occurred_at, reason, signature_reference
            FROM market_approval_transition
            WHERE document_identifier = $1 AND document_version = $2 AND market = $3
            ORDER BY id
            """);

        command.Parameters.AddWithValue(subject.Version.DocumentIdentifier);
        command.Parameters.AddWithValue(subject.Version.Version);
        command.Parameters.AddWithValue(subject.Market);

        var transitions = new List<MarketStateTransition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            transitions.Add(new MarketStateTransition(
                subject,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return transitions;
    }

    public async Task<IReadOnlyDictionary<string, string>> StatesForAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        // The latest transition per market, in one round trip rather than one per market.
        await using var command = _source.Value.CreateCommand("""
            SELECT DISTINCT ON (market) market, to_state
            FROM market_approval_transition
            WHERE document_identifier = $1 AND document_version = $2
            ORDER BY market, id DESC
            """);

        command.Parameters.AddWithValue(version.DocumentIdentifier);
        command.Parameters.AddWithValue(version.Version);

        var states = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            states[reader.GetString(0)] = reader.GetString(1);
        }

        return states;
    }

    public async Task<bool> IsSignatureUsedAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        await using var command = _source.Value.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM market_approval_transition WHERE signature_reference = $1)");

        command.Parameters.AddWithValue(reference);

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public async Task AppendAsync(
        MarketStateTransition transition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        await using var command = _source.Value.CreateCommand("""
            INSERT INTO market_approval_transition
                (document_identifier, document_version, market, from_state, to_state, action,
                 actor, occurred_at, reason, signature_reference)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """);

        command.Parameters.AddWithValue(transition.Subject.Version.DocumentIdentifier);
        command.Parameters.AddWithValue(transition.Subject.Version.Version);
        command.Parameters.AddWithValue(transition.Subject.Market);
        command.Parameters.AddWithValue(transition.From);
        command.Parameters.AddWithValue(transition.To);
        command.Parameters.AddWithValue(transition.Action);
        command.Parameters.AddWithValue(transition.Actor);
        command.Parameters.AddWithValue(transition.At);
        command.Parameters.AddWithValue((object?)transition.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)transition.SignatureReference ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_source.IsValueCreated)
        {
            await _source.Value.DisposeAsync();
        }
    }
}
