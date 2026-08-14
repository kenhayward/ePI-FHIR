using System.Security.Claims;
using Epi.ContentCore;
using Epi.Contracts;
using Epi.Governance.Audit;
using Epi.Governance.Configuration;
using Epi.Governance.Events;
using Epi.Governance.Persistence;
using Epi.Iam;
using Epi.Lifecycle;
using Epi.Search;
using Epi.Signature;
using Epi.Templates;
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

        // Claims arrive named as the identity provider issued them. The default mapping
        // rewrites well-known names to legacy WS-* URIs, which silently emptied the roles
        // the platform reads - authorisation then failed with a policy decision of deny and
        // nothing to say the roles had gone missing on the way in.
        options.MapInboundClaims = false;
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

// The state models are configuration, validated before activation like every other
// configuration the platform reads (CAP-CFG-006, ADR-019 decision 3). Read on first use rather
// than at start-up, which is what they have always done - hoisted here only because more than
// one component now needs them, and loading a model twice could load two different models.
var lifecycleStates = builder.Configuration["Epi:Lifecycle:StatesPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "config", "lifecycle", "label-states.json");
var marketStates = builder.Configuration["Epi:Lifecycle:MarketStatesPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "config", "lifecycle",
        "market-approval-states.json");

var labelModel = new Lazy<LifecycleModel>(() => LifecycleModelConfiguration.LoadFrom(lifecycleStates));
var marketModel = new Lazy<LifecycleModel>(() => LifecycleModelConfiguration.LoadFrom(marketStates));

// Search is served from a projection, derived and never a source of truth (ADR-022 decision 6).
// One instance behind both ports: the thing that is written and the thing that is read are the
// same index, and registering them separately would let a deployment search an empty one.
builder.Services.AddSingleton<InMemorySearchIndex>(services =>
    new InMemorySearchIndex(services.GetRequiredService<IdentifierAuthority>()));
builder.Services.AddSingleton<ISearchProjection>(
    services => services.GetRequiredService<InMemorySearchIndex>());
builder.Services.AddSingleton<ILabelSearch>(
    services => services.GetRequiredService<InMemorySearchIndex>());

builder.Services.AddSingleton<IPermittedScopes>(services =>
    new PolicyPermittedScopes(services.GetRequiredService<IPolicyDecisionPoint>()));

// Held as locals as well as in the container, because the projection decorators wrap them and
// a durable store still has to be found - and initialised - through its own type.
var fhirServer = builder.Configuration["Epi:Content:FhirServerUrl"];
var contentStore = string.IsNullOrWhiteSpace(fhirServer)
    ? new InMemoryContentStore()
    : (IContentStore)new FhirRestContentStore(FhirContentClient.Create(fhirServer));

builder.Services.AddSingleton(services => new ProjectingContentStore(
    contentStore, services.GetRequiredService<ISearchProjection>(), labelModel.Value.Initial));
builder.Services.AddSingleton<IContentStore>(
    services => services.GetRequiredService<ProjectingContentStore>());
// The pinned conformance packages are found by walking up to the repository root when this
// runs from a checkout, which a container has no way to do - it holds the application, not
// the repository. Configuration names the directory where there is no root to find.
builder.Services.AddSingleton(_ => new StructuralValidator(
    ProfileSource.FromPinnedPackages(builder.Configuration["Epi:Validation:PackagesPath"])));

// Governance records - lifecycle, per-market approval, and signatures - share one database.
// They are separate tables for the reasons ADR-005 and ADR-019 give; they are one connection
// because they are one operational store.
var governanceConnection = builder.Configuration["Epi:Governance:ConnectionString"];
var lifecycleStore = string.IsNullOrWhiteSpace(governanceConnection)
    ? new InMemoryLifecycleStore()
    : (ILifecycleStore)new PostgresLifecycleStore(governanceConnection);
var marketApprovalStore = string.IsNullOrWhiteSpace(governanceConnection)
    ? new InMemoryMarketApprovalStore()
    : (IMarketApprovalStore)new PostgresMarketApprovalStore(governanceConnection);
var signatureStore = string.IsNullOrWhiteSpace(governanceConnection)
    ? new InMemorySignatureStore()
    : (ISignatureStore)new PostgresSignatureStore(governanceConnection);
// The same object behind both ports: a pin is written by the append that records the
// transition it belongs to, so they are one store (ADR-024 decision 2).
builder.Services.AddSingleton((IPinnedContextStore)lifecycleStore);

// What the platform validates against, read once and recorded at every approval (ADR-023).
// Lazy for the same reason the state models are: a host that never approves anything need not
// have the vendored packages present.
var packagesDirectory = ProfileSource.PackagesDirectory(
    builder.Configuration["Epi:Validation:PackagesPath"]);
var conformance = new Lazy<ConformanceManifest>(
    () => ConformanceManifest.LoadFrom(packagesDirectory));

// The lifecycle store is wrapped so that a transition recorded by any engine reaches the
// projection, rather than every caller of a transition remembering to update the index.
builder.Services.AddSingleton<ILifecycleStore>(services => new ProjectingLifecycleStore(
    lifecycleStore, services.GetRequiredService<ISearchProjection>()));
builder.Services.AddSingleton(marketApprovalStore);
builder.Services.AddSingleton(signatureStore);

// A signature is spent platform-wide. Neither store can see the other's records, so both
// engines are given the pair rather than their own store alone (CAP-LCM-012).
builder.Services.AddSingleton<ISpentSignatures>(services => new SpentSignatures(
    services.GetRequiredService<ILifecycleStore>(),
    services.GetRequiredService<IMarketApprovalStore>()));
builder.Services.AddSingleton<ISignatureCheck>(services =>
    new SignatureCheck(services.GetRequiredService<ISignatureStore>()));

builder.Services.AddSingleton(services => new LifecycleService(
    labelModel.Value,
    services.GetRequiredService<ILifecycleStore>(),
    time: null,
    signatureCheck: services.GetRequiredService<ISignatureCheck>(),
    spent: services.GetRequiredService<ISpentSignatures>()));

// Signing needs an identity provider to check credentials against. Where none is configured
// the verifier refuses everyone rather than accepting anyone: a permissive default here would
// be a control that is not one, and it would look like it was working (ADR-020 decision 1).
var signingRealm = builder.Configuration["Epi:Signing:Realm"];
var signingAuthority = builder.Configuration["Epi:Signing:KeycloakUrl"];
builder.Services.AddSingleton<ICredentialVerifier>(_ =>
    string.IsNullOrWhiteSpace(signingAuthority) || string.IsNullOrWhiteSpace(signingRealm)
        ? new NoIdentityProvider()
        : new KeycloakCredentialVerifier(
            new HttpClient { BaseAddress = new Uri(signingAuthority) },
            signingRealm,
            builder.Configuration["Epi:Signing:ClientId"] ?? "epi-signing"));

// Auditing is a decorator, so no signing path can avoid being recorded (ADR-018).
builder.Services.AddSingleton<IElectronicSignatureService>(services => new AuditingSignatureService(
    new ElectronicSignatureService(
        services.GetRequiredService<ICredentialVerifier>(),
        services.GetRequiredService<ISignatureStore>()),
    services.GetRequiredService<IAuditSink>()));

builder.Services.AddSingleton(services => new MarketApprovalService(
    marketModel.Value,
    services.GetRequiredService<IMarketApprovalStore>(),
    services.GetRequiredService<MarketCatalogue>().Markets
        .Select(market => market.Code)
        .ToHashSet(StringComparer.Ordinal),
    time: null,
    signatureCheck: services.GetRequiredService<ISignatureCheck>(),
    spent: services.GetRequiredService<ISpentSignatures>()));

// Which state means a market has approved a version is configuration, not a literal in the
// search code (ADR-022 decision 7). A market approval model that names none cannot express
// approval at all, which for this platform is a misconfiguration rather than a variation - so
// it is refused at start-up rather than discovered as a null at the first regulatory question.
builder.Services.AddSingleton(services => new CurrentApprovedVersions(
    services.GetRequiredService<ILabelSearch>(),
    services.GetRequiredService<IMarketApprovalStore>(),
    marketModel.Value.ApprovedState ?? throw new InvalidOperationException(
        $"{marketStates}: 'approvedState' must name the state that means a market has "
        + "approved a version, or the platform cannot answer which version a market currently "
        + "has approved (CAP-SCH-002).")));

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

if (string.IsNullOrWhiteSpace(signingAuthority) || string.IsNullOrWhiteSpace(signingRealm))
{
    app.Logger.LogWarning(
        "No identity provider is configured for signing, so every signature will be refused. "
        + "Approval gates cannot be passed until Epi:Signing:KeycloakUrl and Epi:Signing:Realm "
        + "are set.");
}

// The governance schema is applied as ordered, recorded migrations rather than as a
// per-store bootstrap. CREATE TABLE IF NOT EXISTS does nothing to a table that already
// exists, so the old arrangement could not add a column to a database that predated it -
// and CI, which starts empty every time, could not see the difference (ADR-024 decision 5).
// A failed migration is a start-up failure: a service running against a schema it could not
// fully apply is a service whose writes may or may not land.
if (!string.IsNullOrWhiteSpace(governanceConnection))
{
    await GovernanceSchema.ApplyAsync(governanceConnection);
}

if (!string.IsNullOrWhiteSpace(auditConnection) && auditConnection != governanceConnection)
{
    // The audit trail may be held apart from the rest of the governance records.
    await GovernanceSchema.ApplyAsync(auditConnection);
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
    IdentifierAuthority authority,
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

        // Identity is minted before anything is written, so the version can be registered
        // under lifecycle management before its content exists (ADR-025 decision 1).
        var identity = ContentIdentity.Mint(authority);

        // Composed here so no endpoint can reach the raw store by accident: scope on every
        // operation, validation on the way in.
        // Auditing outermost so a rejected write is recorded, then events after a successful
        // one, then scope, then validation, then registration closest to the store - so
        // content that is invalid or out of scope never reaches registration, and nothing is
        // stored that was not registered first (ADR-025 decision 3).
        // Materialising outermost of the content concerns, so what validation sees is what is
        // stored, and resolving units through the caller's own scoped store, so borrowing
        // cannot be used to read a unit they may not see (ADR-026 decision 4).
        var gated = new AuditingContentStore(
            new PublishingContentStore(
                new MaterialisingContentStore(
                    new ValidatingContentStore(
                        new ScopedContentStore(
                            new RegisteringContentStore(store, lifecycle, subject.Id),
                            policy, subject),
                        validator),
                    new ScopedContentStore(store, policy, subject),
                    authority),
                events),
            audit,
            subject.Id);

        var stored = await gated.CreateAsync(identity, bundle, cancellationToken);

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
    catch (UnitNotAvailableException unavailable)
    {
        // 400, and without saying whether the unit exists: a caller must not learn from a
        // borrow that there is a unit they may not see (ADR-026, CAP-SCH-004).
        return Results.BadRequest(new { problems = new[] { unavailable.Message } });
    }
    catch (VersionConflictException conflict)
    {
        return Results.Problem(conflict.Message, statusCode: StatusCodes.Status409Conflict);
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
    DateTimeOffset? asAt,
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

    // "As at" is derived from the append-only history rather than read from a field, which is
    // the question an inspection actually asks (CAP-LCM-006, ADR-023).
    var state = asAt is null
        ? await lifecycle.CurrentStateAsync(reference, cancellationToken)
        : await lifecycle.StateAtAsync(reference, asAt.Value, cancellationToken);

    return state is null
        ? Results.NotFound()
        : Results.Ok(new
        {
            identifier = id,
            version,
            state,
            asAt,
            author = await lifecycle.AuthorOfAsync(reference, cancellationToken),

            // Named separately and always, so "approved" can never be read as approved
            // everywhere (ADR-005).
            markets = await markets.StatesAsync(reference, cancellationToken),
        });
}).RequireAuthorization();

app.MapPost("/signatures", async (
    SignatureRequest body,
    ClaimsPrincipal principal,
    IContentStore store,
    IPolicyDecisionPoint policy,
    IElectronicSignatureService signing,
    IdentifierAuthority authority,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    if (!Enum.TryParse<SignatureMeaning>(body.Meaning, ignoreCase: true, out var meaning))
    {
        return Results.BadRequest(new
        {
            problems = new[]
            {
                $"'{body.Meaning}' is not a signature meaning. Permitted meanings are "
                + string.Join(", ", Enum.GetNames<SignatureMeaning>()) + ".",
            },
        });
    }

    // Read through scope: a signer may only sign content they are allowed to see. Otherwise
    // signing becomes a way of discovering that a document exists, and of attesting to
    // content the signer was never permitted to read.
    var scoped = new ScopedContentStore(store, policy, subject);
    var document = await scoped.GetAsync(
        new DocumentIdentity(authority.DocumentSystem, body.DocumentIdentifier),
        body.Version,
        cancellationToken);

    if (document is null)
    {
        return Results.NotFound();
    }

    try
    {
        // The username is the authenticated caller's, never the request body's: a caller able
        // to name the signer could sign as somebody else (ADR-020 decision 3). It is the
        // username rather than the subject because that is what an identity provider
        // authenticates; the manifest still records the subject the credentials proved.
        var manifest = await signing.SignAsync(
            document, subject.Username, body.Password, meaning, body.Reason, cancellationToken);

        return Results.Ok(new
        {
            reference = manifest.Reference,
            signer = manifest.SignerIdentifier,
            printedName = manifest.SignerPrintedName,
            meaning = manifest.Meaning.ToString(),
            contentHash = manifest.ContentHash,
            signedAt = manifest.SignedAt,
        });
    }
    catch (SignatureRefusedException refused)
    {
        // One message for every refusal, so an approval screen cannot be used to work out
        // who holds an account.
        return Results.Problem(refused.Reason, statusCode: StatusCodes.Status403Forbidden);
    }
}).RequireAuthorization();

app.MapPost("/labels/{id}/versions/{version:int}/transitions", async (
    string id,
    int version,
    TransitionRequest body,
    ClaimsPrincipal principal,
    IContentStore store,
    IPolicyDecisionPoint policy,
    LifecycleService lifecycle,
    IPinnedContextStore pins,
    IdentifierAuthority authority,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    var scoped = new ScopedContentStore(store, policy, subject);
    var document = await scoped.GetAsync(
        new DocumentIdentity(authority.DocumentSystem, id), version, cancellationToken);

    if (document is null)
    {
        return Results.NotFound();
    }

    try
    {
        // Approval is the moment the organisation commits to a version, and the moment what it
        // was approved against has to be written down: every part of that is configuration, and
        // configuration moves (CAP-LCM-011, ADR-023 decision 1). The ingredients are supplied
        // here and the engine decides whether a pin is due, so the pin lands in the same
        // transaction as the transition (ADR-024 decision 3).
        var transition = await lifecycle.TransitionAsync(
            new VersionRef(id, version), body.Action, subject.Id, body.Reason,
            body.SignatureReference,
            Pinning.ContextFor(document, conformance, authority),
            cancellationToken);

        return Results.Ok(new
        {
            from = transition.From,
            to = transition.To,
            action = transition.Action,
            actor = transition.Actor,
            at = transition.At,
        });
    }
    catch (TransitionRefusedException refused)
    {
        // 409 rather than 400: the request is well formed and the platform understood it. It
        // is the state of the version, or who is asking, that makes it impossible.
        return Results.Problem(refused.Reason, statusCode: StatusCodes.Status409Conflict);
    }
}).RequireAuthorization();

app.MapGet("/labels/search", async (
    string? text,
    string? product,
    string? market,
    string? language,
    string? state,
    string? identifier,
    int? page,
    int? pageSize,
    ClaimsPrincipal principal,
    IPermittedScopes permitted,
    ILabelSearch search,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    // Scope first, then the query. The permitted set bounds the search rather than filtering
    // its results, so the total is a true total and a page is a full page (ADR-022 decision 1).
    var scopes = await permitted.ForAsync(subject, "read", cancellationToken);

    var results = await search.SearchAsync(
        new ScopedSearchQuery(
            new SearchCriteria(
                text, product, market, language, state, identifier,
                page ?? 1, pageSize ?? SearchCriteria.DefaultPageSize),
            scopes),
        cancellationToken);

    return Results.Ok(new
    {
        total = results.Total,
        page = results.Page,
        pageSize = results.PageSize,
        hits = results.Hits.Select(Describe),
    });
}).RequireAuthorization();

app.MapGet("/labels/{id}/current-approved", async (
    string id,
    string market,
    ClaimsPrincipal principal,
    IPermittedScopes permitted,
    CurrentApprovedVersions approved,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    var scopes = await permitted.ForAsync(subject, "read", cancellationToken);
    var hit = await approved.ForAsync(id, market, scopes, cancellationToken);

    // Not found covers both "this market has approved nothing" and "you may not see this
    // document", deliberately: the second must not be distinguishable from the first
    // (CAP-SCH-004).
    return hit is null ? Results.NotFound() : Results.Ok(Describe(hit));
}).RequireAuthorization();

app.MapPost("/labels/{id}/versions/{version:int}/markets/{market}/transitions", async (
    string id,
    int version,
    string market,
    TransitionRequest body,
    ClaimsPrincipal principal,
    IContentStore store,
    IPolicyDecisionPoint policy,
    MarketApprovalService markets,
    IdentifierAuthority authority,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    // Scope is decided on the content, as everywhere else: a market approval record carries no
    // affiliate of its own, and answering from it alone would act on documents the caller may
    // not see.
    var scoped = new ScopedContentStore(store, policy, subject);
    var document = await scoped.GetAsync(
        new DocumentIdentity(authority.DocumentSystem, id), version, cancellationToken);

    if (document is null)
    {
        return Results.NotFound();
    }

    // Seeing a label is not the same as being allowed to deal with a regulator about it, so
    // the move itself is authorised as well as the read. The two actions are the distinction
    // the market model already draws: a transition requiring a signature is an act of this
    // organisation by an accountable person, and one that does not is the recording of a
    // decision taken outside it (CAP-LCM-012, ADR-020).
    var scope = ContentScope.Of(document.Bundle, authority)!;
    var permission = markets.RequiresSignature(body.Action)
        ? "submit-to-regulator"
        : "record-decision";

    var decision = await policy.DecideAsync(
        new AuthorizationQuery(subject, permission, new ResourceScope(scope.Affiliate, scope.Market)),
        cancellationToken);

    if (!decision.Allowed)
    {
        return Results.Problem(
            $"Access denied for action '{permission}': {decision.Reason}",
            statusCode: StatusCodes.Status403Forbidden);
    }

    try
    {
        var transition = await markets.TransitionAsync(
            new VersionRef(id, version), market, body.Action, subject.Id, body.Reason,
            body.SignatureReference, cancellationToken);

        return Results.Ok(new
        {
            market = transition.Subject.Market,
            from = transition.From,
            to = transition.To,
            action = transition.Action,
            actor = transition.Actor,
            at = transition.At,
        });
    }
    catch (TransitionRefusedException refused)
    {
        return Results.Problem(refused.Reason, statusCode: StatusCodes.Status409Conflict);
    }
}).RequireAuthorization();

app.MapPost("/fhir/Bundle/{id}/versions", async (
    string id,
    HttpRequest request,
    ClaimsPrincipal principal,
    IContentStore store,
    StructuralValidator validator,
    IPolicyDecisionPoint policy,
    IAuditSink audit,
    IEventPublisher events,
    LifecycleService lifecycle,
    IdentifierAuthority authority,
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
        var identity = new DocumentIdentity(authority.DocumentSystem, id);

        // Read through scope first. Without it, an unknown document and one the caller may not
        // see would answer differently, and adding a version would be a way of proving that a
        // document exists (CAP-SCH-004).
        var scoped = new ScopedContentStore(store, policy, subject);
        if (await scoped.GetLatestAsync(identity, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        // The version this write believes it is creating. Stated rather than assigned, so two
        // authors racing to create the same next version get a refusal that names the conflict
        // rather than a silent interleave (ADR-025 decision 4).
        var next = (await scoped.VersionsAsync(identity, cancellationToken))[^1] + 1;

        // Materialising outermost of the content concerns, so what validation sees is what is
        // stored, and resolving units through the caller's own scoped store, so borrowing
        // cannot be used to read a unit they may not see (ADR-026 decision 4).
        var gated = new AuditingContentStore(
            new PublishingContentStore(
                new MaterialisingContentStore(
                    new ValidatingContentStore(
                        new ScopedContentStore(
                            new RegisteringContentStore(store, lifecycle, subject.Id),
                            policy, subject),
                        validator),
                    new ScopedContentStore(store, policy, subject),
                    authority),
                events),
            audit,
            subject.Id);

        var stored = await gated.CreateVersionAsync(identity, next, bundle, cancellationToken);

        return Results.Created(
            $"/fhir/Bundle/{stored.Identity.Value}/versions/{stored.Version}",
            new
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
    catch (UnknownDocumentException)
    {
        return Results.NotFound();
    }
    catch (UnitNotAvailableException unavailable)
    {
        // 400 rather than 404: the request named a unit this caller cannot borrow from, and
        // saying which would tell them whether it exists (ADR-026, CAP-SCH-004).
        return Results.BadRequest(new { problems = new[] { unavailable.Message } });
    }
    catch (VersionConflictException conflict)
    {
        // 409, and the caller is told which version was taken: the request was well formed and
        // somebody else got there first.
        return Results.Problem(conflict.Message, statusCode: StatusCodes.Status409Conflict);
    }
}).RequireAuthorization();

app.MapGet("/fhir/Bundle/{id}/versions/{version:int}", async (
    string id,
    int version,
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

    // The historical content itself, unchanged since it was stored. Reconstruction is worthless
    // while the only reachable content is the latest version (ADR-023 decision 6).
    var scoped = new ScopedContentStore(store, policy, subject);
    var document = await scoped.GetAsync(
        new DocumentIdentity(authority.DocumentSystem, id), version, cancellationToken);

    return document is null
        ? Results.NotFound()
        : Results.Content(EpiBundleReader.Write(document.Bundle), "application/fhir+json");
}).RequireAuthorization();

app.MapGet("/labels/{id}/versions/{version:int}/reconstruction", async (
    string id,
    int version,
    ClaimsPrincipal principal,
    IContentStore store,
    IPolicyDecisionPoint policy,
    LifecycleService lifecycle,
    MarketApprovalService markets,
    IPinnedContextStore pins,
    ISignatureStore signatures,
    IdentifierAuthority authority,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    var scoped = new ScopedContentStore(store, policy, subject);
    var document = await scoped.GetAsync(
        new DocumentIdentity(authority.DocumentSystem, id), version, cancellationToken);

    if (document is null)
    {
        return Results.NotFound();
    }

    var reference = new VersionRef(id, version);
    var state = await lifecycle.CurrentStateAsync(reference, cancellationToken);
    if (state is null)
    {
        return Results.NotFound();
    }

    var pinned = await pins.ForAsync(reference, cancellationToken);

    // Reported, never enforced, and never recomputed: the platform says what was recorded and
    // whether the packages present now are still those bytes. A verdict produced today with
    // today's packages would be evidence about today (ADR-023 decisions 4 and 5).
    var discrepancies = pinned is null
        ? []
        : conformance.Value.Discrepancies(packagesDirectory);

    var history = new List<object>();
    foreach (var transition in await lifecycle.HistoryAsync(reference, cancellationToken))
    {
        var manifest = transition.SignatureReference is null
            ? null
            : await signatures.FindAsync(transition.SignatureReference, cancellationToken);

        history.Add(new
        {
            from = transition.From,
            to = transition.To,
            action = transition.Action,
            actor = transition.Actor,
            at = transition.At,
            reason = transition.Reason,
            signature = manifest is null ? null : new
            {
                reference = manifest.Reference,
                signer = manifest.SignerIdentifier,
                printedName = manifest.SignerPrintedName,
                meaning = manifest.Meaning.ToString(),
                contentHash = manifest.ContentHash,
                signedAt = manifest.SignedAt,
            },
        });
    }

    return Results.Ok(new
    {
        identifier = id,
        version,
        state,
        author = await lifecycle.AuthorOfAsync(reference, cancellationToken),
        contentHash = ContentHash.Of(document.Bundle),
        pinnedContext = pinned is null ? null : new
        {
            contentHash = pinned.ContentHash,
            stateModel = pinned.StateModel,
            state = pinned.State,
            identifierAuthority = pinned.IdentifierAuthority,
            template = pinned.Template,
            templateVersion = pinned.TemplateVersion,
            pinnedAt = pinned.PinnedAt,
            packages = pinned.Packages.Select(p => new
            {
                name = p.Name,
                version = p.Version,
                sha256 = p.Sha256,
            }),
        },
        packagesStillMatch = discrepancies.Count == 0,
        packageDiscrepancies = discrepancies,
        markets = await markets.StatesAsync(reference, cancellationToken),
        history,
    });
}).RequireAuthorization();

app.Run();

/// <summary>One search result on the wire.</summary>
static object Describe(SearchHit hit) => new
{
    documentIdentifier = hit.DocumentIdentifier,
    version = hit.Version,
    title = hit.Title,
    affiliate = hit.Scope.Affiliate,
    market = hit.Scope.Market,
    state = hit.State,
    language = hit.Language,
    product = hit.Product,
    documentType = hit.DocumentType,
};

/// <summary>
/// The ingredients an approval is pinned from (ADR-023, ADR-024 decision 3).
/// </summary>
/// <remarks>
/// Assembled here because this is where content and configuration meet; the decision about
/// whether a pin is due belongs to the lifecycle engine, which is the thing that knows the
/// transition lands on the approved state. Supplied on every transition rather than only on
/// approvals, so no caller has to work out which ones are approvals - getting that wrong is
/// how the pin would go missing.
/// </remarks>
static class Pinning
{
    public static ApprovalContext ContextFor(
        EpiDocument document,
        Lazy<ConformanceManifest> conformance,
        IdentifierAuthority authority) =>
        new(ContentHash.Of(document.Bundle),
            [.. conformance.Value.Packages.Select(p => new PinnedPackage(p.Name, p.Version, p.Sha256))],
            authority.DocumentSystem,
            TemplateInstantiation.TemplateOf(document.Bundle, authority),
            TemplateInstantiation.TemplateVersionOf(document.Bundle, authority));
}

/// <summary>What a caller asks for when signing. The signer is the token, never the body.</summary>
internal sealed record SignatureRequest(
    string DocumentIdentifier, int Version, string Meaning, string Password, string? Reason);

/// <summary>What a caller asks for when moving a version between states.</summary>
internal sealed record TransitionRequest(string Action, string? Reason, string? SignatureReference);

/// <summary>
/// Stands in where no identity provider is configured. Refuses everyone, so a deployment that
/// forgot to configure signing cannot sign rather than signing freely.
/// </summary>
internal sealed class NoIdentityProvider : ICredentialVerifier
{
    public Task<SignerIdentity?> VerifyAsync(
        string identifier, string password, CancellationToken cancellationToken = default) =>
        Task.FromResult<SignerIdentity?>(null);
}

// Exposed so the test host can reference the entry point generated from top-level statements.
public partial class Program;
