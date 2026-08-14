using System.Security.Claims;
using Epi.ContentCore;
using Epi.Contracts;
using Epi.Governance.Audit;
using Epi.Governance.Configuration;
using Epi.Governance.Events;
using Epi.Governance.Persistence;
using Epi.Iam;
using Epi.Lifecycle;
using Epi.Signature;
using Epi.Validation;
using Microsoft.AspNetCore.Authentication.JwtBearer;

// Epi.Api - the HTTP host for the ePI platform.
//
// The walking skeleton of design/iteration-1.md: authenticate, authorise, validate, store.

var builder = WebApplication.CreateBuilder(args);

// Authentication is delegated entirely to the enterprise identity provider (CAP-IAM-001).
// There is deliberately no local credential path and no development shortcut here to be left
// switched on by accident: the platform never authenticates anyone itself.
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Epi:Authentication:Authority"];
        options.Audience = builder.Configuration["Epi:Authentication:Audience"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
    });
builder.Services.AddAuthorization();

// Market configuration is loaded at start-up and fails fast: invalid configuration must not
// reach a running service (CAP-CFG-006).
builder.Services.AddSingleton(_ => MarketCatalogue.LoadFrom(
    builder.Configuration["Epi:MarketsPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "config", "markets")));

builder.Services.AddSingleton<IPolicyDecisionPoint>(_ => new OpaPolicyDecisionPoint(
    new HttpClient
    {
        BaseAddress = new Uri(builder.Configuration["Epi:Authorization:OpaUrl"] ?? "http://localhost:8181"),
    }));

// The identifier authority is configuration, never a literal (ADR-017). A deployment that
// has not set it runs on a namespace nobody owns, which is the intended, conspicuous default.
builder.Services.AddSingleton(_ => IdentifierAuthorityConfiguration.LoadFrom(
    builder.Configuration["Epi:IdentifiersPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "config", "identifiers.json")));

// Each dependency is real when it is configured and a reference implementation when it is not,
// so the platform runs unattended for a demonstration and against the stack for anything else.
// The choice is logged at start-up: a service quietly holding its audit trail in memory is the
// kind of thing nobody notices until an inspection asks for it.
var auditConnection = builder.Configuration["Epi:Audit:ConnectionString"];
builder.Services.AddSingleton<IAuditSink>(_ => string.IsNullOrWhiteSpace(auditConnection)
    ? new InMemoryAuditSink()
    : new PostgresAuditSink(auditConnection));

var brokers = builder.Configuration["Epi:Events:BootstrapServers"];
builder.Services.AddSingleton<IEventPublisher>(_ => string.IsNullOrWhiteSpace(brokers)
    ? new InMemoryEventPublisher()
    : new KafkaEventPublisher(brokers, builder.Configuration["Epi:Events:Topic"]));

var fhirServer = builder.Configuration["Epi:Content:FhirServerUrl"];
builder.Services.AddSingleton<IContentStore>(_ => string.IsNullOrWhiteSpace(fhirServer)
    ? new InMemoryContentStore()
    : new FhirRestContentStore(FhirContentClient.Create(fhirServer)));
builder.Services.AddSingleton(_ => new StructuralValidator(ProfileSource.FromPinnedPackages()));

// Governance records - lifecycle, per-market approval, and signatures - share one database.
// They are separate tables for the reasons ADR-005 and ADR-019 give; they are one connection
// because they are one operational store.
var governanceConnection = builder.Configuration["Epi:Governance:ConnectionString"];
builder.Services.AddSingleton<ILifecycleStore>(_ => string.IsNullOrWhiteSpace(governanceConnection)
    ? new InMemoryLifecycleStore()
    : new PostgresLifecycleStore(governanceConnection));
builder.Services.AddSingleton<IMarketApprovalStore>(_ => string.IsNullOrWhiteSpace(governanceConnection)
    ? new InMemoryMarketApprovalStore()
    : new PostgresMarketApprovalStore(governanceConnection));
builder.Services.AddSingleton<ISignatureStore>(_ => string.IsNullOrWhiteSpace(governanceConnection)
    ? new InMemorySignatureStore()
    : new PostgresSignatureStore(governanceConnection));

// A signature is spent platform-wide. Neither store can see the other's records, so both
// engines are given the pair rather than their own store alone (CAP-LCM-012).
builder.Services.AddSingleton<ISpentSignatures>(services => new SpentSignatures(
    services.GetRequiredService<ILifecycleStore>(),
    services.GetRequiredService<IMarketApprovalStore>()));
builder.Services.AddSingleton<ISignatureCheck>(services =>
    new SignatureCheck(services.GetRequiredService<ISignatureStore>()));

// The state models are configuration, loaded and validated at start-up like every other
// configuration the platform reads (CAP-CFG-006, ADR-019 decision 3).
var lifecycleStates = builder.Configuration["Epi:Lifecycle:StatesPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "config", "lifecycle", "label-states.json");
var marketStates = builder.Configuration["Epi:Lifecycle:MarketStatesPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "config", "lifecycle", "market-approval-states.json");

builder.Services.AddSingleton(services => new LifecycleService(
    LifecycleModelConfiguration.LoadFrom(lifecycleStates),
    services.GetRequiredService<ILifecycleStore>(),
    time: null,
    signatureCheck: services.GetRequiredService<ISignatureCheck>(),
    spent: services.GetRequiredService<ISpentSignatures>()));

builder.Services.AddSingleton(services => new MarketApprovalService(
    LifecycleModelConfiguration.LoadFrom(marketStates),
    services.GetRequiredService<IMarketApprovalStore>(),
    services.GetRequiredService<MarketCatalogue>().Markets
        .Select(market => market.Code)
        .ToHashSet(StringComparer.Ordinal),
    time: null,
    signatureCheck: services.GetRequiredService<ISignatureCheck>(),
    spent: services.GetRequiredService<ISpentSignatures>()));

var app = builder.Build();

if (string.IsNullOrWhiteSpace(auditConnection) || string.IsNullOrWhiteSpace(brokers)
    || string.IsNullOrWhiteSpace(fhirServer) || string.IsNullOrWhiteSpace(governanceConnection))
{
    app.Logger.LogWarning(
        "Running with in-memory components (content: {Content}, audit: {Audit}, events: {Events}, "
        + "governance: {Governance}). Nothing is durable. This is a demonstration default, not a "
        + "deployment.",
        string.IsNullOrWhiteSpace(fhirServer) ? "in-memory" : "FHIR server",
        string.IsNullOrWhiteSpace(auditConnection) ? "in-memory" : "PostgreSQL",
        string.IsNullOrWhiteSpace(brokers) ? "in-memory" : "Kafka",
        string.IsNullOrWhiteSpace(governanceConnection) ? "in-memory" : "PostgreSQL");
}

// The audit table and its append-only trigger must exist before the first record. In a
// qualified environment this belongs in a controlled migration (D3 Section 10.3).
if (app.Services.GetRequiredService<IAuditSink>() is PostgresAuditSink durableAudit)
{
    await durableAudit.InitialiseAsync();
}

if (app.Services.GetRequiredService<ILifecycleStore>() is PostgresLifecycleStore durableLifecycle)
{
    await durableLifecycle.InitialiseAsync();
}

if (app.Services.GetRequiredService<IMarketApprovalStore>() is PostgresMarketApprovalStore durableMarkets)
{
    await durableMarkets.InitialiseAsync();
}

if (app.Services.GetRequiredService<ISignatureStore>() is PostgresSignatureStore durableSignatures)
{
    await durableSignatures.InitialiseAsync();
}

app.UseAuthentication();
app.UseAuthorization();

// Liveness probe for the container platform. Deliberately unauthenticated and free of
// dependency checks: it answers "is this process serving?", not "is the platform well?".
// Requiring a token here would make an identity-provider outage look like a dead service.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "epi-api" }));

app.MapPost("/fhir/Bundle", async (
    HttpRequest request,
    ClaimsPrincipal principal,
    IContentStore store,
    StructuralValidator validator,
    IPolicyDecisionPoint policy,
    IAuditSink audit,
    IEventPublisher events,
    LifecycleService lifecycle,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    using var reader = new StreamReader(request.Body);
    var json = await reader.ReadToEndAsync(cancellationToken);

    try
    {
        var bundle = EpiBundleReader.Read(json);

        // Composed here so no endpoint can reach the raw store by accident: scope on every
        // operation, validation on the way in.
        // Auditing outermost so a rejected write is recorded, then events after a successful
        // one, then scope, then validation closest to the store.
        var gated = new AuditingContentStore(
            new PublishingContentStore(
                new ValidatingContentStore(
                    new ScopedContentStore(store, policy, subject), validator),
                events),
            audit,
            subject.Id);

        var stored = await gated.CreateAsync(bundle, cancellationToken);

        // Placed under lifecycle management the moment it exists. A stored version with no
        // state has no author either, and the author is what segregation of duties is checked
        // against - so a window where content exists unmanaged is a window where nobody wrote
        // it (CAP-LCM-001, CAP-IAM-006).
        await lifecycle.RegisterAsync(
            new VersionRef(stored.Identity.Value, stored.Version), subject.Id, cancellationToken);

        return Results.Created($"/fhir/Bundle/{stored.Identity.Value}", new
        {
            identifier = stored.Identity.Value,
            system = stored.Identity.System,
            version = stored.Version,
        });
    }
    catch (InvalidEpiBundleException invalid)
    {
        return Results.BadRequest(new { problems = invalid.Problems });
    }
    catch (ContentRejectedException rejected)
    {
        return Results.BadRequest(new
        {
            problems = rejected.Issues.Select(i => new
            {
                severity = i.Severity.ToString(),
                i.Location,
                i.Message,
            }),
        });
    }
    catch (AccessDeniedException denied)
    {
        return Results.Problem(denied.Message, statusCode: StatusCodes.Status403Forbidden);
    }
}).RequireAuthorization();

app.MapGet("/fhir/Bundle/{id}", async (
    string id,
    ClaimsPrincipal principal,
    IContentStore store,
    IPolicyDecisionPoint policy,
    IdentifierAuthority authority,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    var scoped = new ScopedContentStore(store, policy, subject);
    var document = await scoped.GetLatestAsync(
        new DocumentIdentity(authority.DocumentSystem, id), cancellationToken);

    // Out of scope is indistinguishable from absent, so a caller cannot learn that a document
    // they may not see exists (CAP-SCH-004).
    return document is null
        ? Results.NotFound()
        : Results.Content(EpiBundleReader.Write(document.Bundle), "application/fhir+json");
}).RequireAuthorization();

app.MapGet("/labels/{id}/versions/{version:int}/state", async (
    string id,
    int version,
    ClaimsPrincipal principal,
    IContentStore store,
    IPolicyDecisionPoint policy,
    LifecycleService lifecycle,
    MarketApprovalService markets,
    IdentifierAuthority authority,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    // Scope is decided on the content, not on the state record: a state record carries no
    // affiliate or market of its own, and answering from it alone would report on documents
    // the caller may not see. Out of scope is indistinguishable from absent (CAP-SCH-004).
    var scoped = new ScopedContentStore(store, policy, subject);
    var document = await scoped.GetAsync(
        new DocumentIdentity(authority.DocumentSystem, id), version, cancellationToken);

    if (document is null)
    {
        return Results.NotFound();
    }

    var reference = new VersionRef(id, version);
    var state = await lifecycle.CurrentStateAsync(reference, cancellationToken);

    return state is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            identifier = id,
            version,
            state,
            author = await lifecycle.AuthorOfAsync(reference, cancellationToken),

            // Named separately and always, so "approved" can never be read as approved
            // everywhere (ADR-005).
            markets = await markets.StatesAsync(reference, cancellationToken),
        });
}).RequireAuthorization();

app.Run();

// Exposed so the test host can reference the entry point generated from top-level statements.
public partial class Program;
