using System.Text;
using System.Security.Claims;
using Epi.ContentCore;
using Epi.Contracts;
using Epi.Governance.Audit;
using Epi.Governance.Configuration;
using Epi.Governance.Events;
using Epi.Governance.Persistence;
using Epi.Iam;
using Epi.Lifecycle;
using Epi.Rendering;
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

var workflowStore = string.IsNullOrWhiteSpace(governanceConnection)
    ? new InMemoryWorkflowStore()
    : (IWorkflowStore)new PostgresWorkflowStore(governanceConnection);
builder.Services.AddSingleton(workflowStore);

// Routing is configuration like every other model the platform applies (ADR-031 decision 3,
// ADR-035 decision 6). A directory of models rather than one file: which process applies is
// selected per label type and market, and adding a market's process is adding a file.
//
// A deployment that has configured none still raises no tasks, and the approval gate is
// unaffected either way: a task never decides whether a transition may happen. What has changed
// is that the absence is now said out loud at start-up, and that a directory which exists but
// cannot be loaded stops the service rather than quietly routing nothing. Routing loading
// silently as absent has already happened once, in a container whose path was never wired.
var routingPath = builder.Configuration["Epi:Workflow:RoutingPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "config", "workflow", "label");
var routingConfigured = Directory.Exists(routingPath);
var routing = new Lazy<WorkflowCatalogue?>(
    () => routingConfigured ? WorkflowCatalogue.LoadFrom(routingPath) : null);

// What the platform validates against, read once and recorded at every approval (ADR-023).
// Lazy for the same reason the state models are: a host that never approves anything need not
// have the vendored packages present.
var packagesDirectory = ProfileSource.PackagesDirectory(
    builder.Configuration["Epi:Validation:PackagesPath"]);
var conformance = new Lazy<ConformanceManifest>(
    () => ConformanceManifest.LoadFrom(packagesDirectory));

// Terminology is reached through a port the platform owns, never a vendor's client (ADR-036
// decision 1). Which server, and which source for which concept domain, is an open programme
// question; this is the seam that keeps the answer a component and a configuration entry.
builder.Services.AddSingleton<ITerminologyDirectory>(
    _ => new PinnedPackageTerminologyDirectory(conformance.Value));

// Master data likewise. The configured directory is the reference implementation, in the same
// way the in-memory stores stand behind their durable counterparts.
builder.Services.AddSingleton<IProductDirectory>(_ => ConfiguredProductDirectory.LoadFrom(
    builder.Configuration["Epi:MasterDataPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "config", "master-data", "products.json")));

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

// Where render templates live (ADR-042). In memory for now, like every other store before its
// durable counterpart lands.
builder.Services.AddSingleton<ITemplateStore>(_ => new InMemoryTemplateStore());

// The system clock, resolvable rather than reached for. Reconciliation compares a registration
// against now, and a test that cannot move "now" cannot test a settle period at all.
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddSingleton(services => new LifecycleService(
    labelModel.Value,
    services.GetRequiredService<ILifecycleStore>(),
    time: null,
    signatureCheck: services.GetRequiredService<ISignatureCheck>(),
    spent: services.GetRequiredService<ISpentSignatures>(),
    workflow: routing.Value,
    tasks: services.GetRequiredService<IWorkflowStore>()));

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

// Every configuration the platform reads from a path, resolved here rather than on first use
// (CAP-CFG-006, FN-CFG-002).
//
// Three defects have now shared one shape, and each was found by running the walkthrough rather
// than by CI: a path that differed only inside a container, so the service started, reported
// healthy, and had silently loaded nothing. The failure appeared later, as something not
// happening - no task raised, no market state model, no routing.
//
// Each loader already refuses a path it cannot read. What was missing is that nothing asked
// them until something needed them, so the refusal arrived as a 500 on somebody's approval
// days after the deployment that caused it. Touching each one here turns all three into a
// failure to start, which is attributable.
//
// The vendored conformance packages are deliberately not among these: a host that never
// validates or approves anything need not have them present, and requiring them would make the
// packages a start-up dependency of every deployment rather than of the work that uses them.
foreach (var configuration in new (string Setting, Func<object> Resolve)[]
{
    ("Epi:MarketsPath", () => app.Services.GetRequiredService<MarketCatalogue>()),
    ("Epi:IdentifiersPath", () => app.Services.GetRequiredService<IdentifierAuthority>()),
    ("Epi:Lifecycle:StatesPath", () => labelModel.Value),
    ("Epi:Lifecycle:MarketStatesPath", () => marketModel.Value),
    ("Epi:MasterDataPath", () => app.Services.GetRequiredService<IProductDirectory>()),
})
{
    try
    {
        configuration.Resolve();
    }
    catch (Exception unreadable)
    {
        // Rethrown with the setting named. A loader says what it could not read; only this
        // knows which setting pointed it there, and that is the half an operator needs.
        throw new InvalidOperationException(
            $"The platform cannot start: configuration for '{configuration.Setting}' could not "
            + $"be loaded. {unreadable.Message}",
            unreadable);
    }
}

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

// Resolved here rather than on the first transition. A routing model that cannot be loaded is
// a configuration error, and one that surfaces as a 500 on somebody's approval - hours or days
// after the deployment - is a configuration error nobody attributes to the deployment.
if (routingConfigured)
{
    app.Logger.LogInformation(
        "Routing models loaded from {Path}: {Models}.",
        routingPath,
        string.Join(", ", routing.Value!.Models.Select(model => model.Name)));
}
else
{
    app.Logger.LogWarning(
        "No routing models were found at {Path}, so no review task will ever be raised and "
        + "nobody will be asked to approve anything. The approval gate still holds; the ask "
        + "simply does not happen.",
        routingPath);
}

// Standard templates a deployment starts from, created as drafts and never as approvals
// (ADR-042 decision 7). Nothing already in the store is touched: it belongs to whoever put it
// there, and a seed correcting it would change what a patient reads without anybody deciding to.
var seededTemplates = await TemplateSeeding.ApplyAsync(
    app.Services.GetRequiredService<ITemplateStore>(),
    builder.Configuration["Epi:TemplateSeedPath"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "config", "templates", "seed"));

app.Logger.LogInformation(
    seededTemplates.Count == 0
        ? "No templates were seeded; the store already holds what it needs."
        : "Seeded {Count} template(s) as drafts: {Templates}. None is approved, and nothing may "
          + "be officially rendered with one until somebody signs for it.",
    seededTemplates.Count,
    string.Join(", ", seededTemplates));

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
                    new CrossReferenceCheckingContentStore(
                        new ValidatingContentStore(
                            new ScopedContentStore(
                                new RegisteringContentStore(store, lifecycle, subject.Id),
                                policy, subject),
                            validator)),
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

            // What may be done to each from here, beside rather than inside the states above:
            // that shape is what callers already read, and answering a second question by
            // changing the answer to the first breaks every one of them. A surface working any
            // of this out would be a second implementation of a state model it cannot see
            // (ADR-037 decision 1).
            marketActions = (await markets.StatesAsync(reference, cancellationToken))
                .ToDictionary(
                    market => market.Key,
                    market => new
                    {
                        actions = marketModel.Value.Transitions
                            .Where(t => string.Equals(t.From, market.Value, StringComparison.Ordinal))
                            .Select(t => t.Action),

                        // Submitting to a regulator is an act of this organisation by an
                        // accountable person; recording what the regulator decided is a factual
                        // entry about somebody else's decision (CAP-LCM-012).
                        signedActions = marketModel.Value.Transitions
                            .Where(t => string.Equals(t.From, market.Value, StringComparison.Ordinal)
                                        && t.RequiresSignature)
                            .Select(t => t.Action),

                        // What each signed act must assert, for the reason above.
                        signatureMeanings = marketModel.Value.Transitions
                            .Where(t => string.Equals(t.From, market.Value, StringComparison.Ordinal)
                                        && t.RequiresSignature && t.SignatureMeaning is not null)
                            .ToDictionary(t => t.Action, t => t.SignatureMeaning!),

                        // The same rule the engine applies: the transition that lands on the
                        // approved state must say when it takes effect, and no other may
                        // (ADR-029 decision 3).
                        actionsNeedingEffectiveDate = marketModel.Value.Transitions
                            .Where(t => string.Equals(t.From, market.Value, StringComparison.Ordinal)
                                        && string.Equals(
                                            t.To, marketModel.Value.ApprovedState,
                                            StringComparison.Ordinal))
                            .Select(t => t.Action),
                    }),
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
    ITerminologyDirectory terminology,
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
        // What the document is and where it is going, read from the content rather than taken
        // from the request: a caller that could state its own label type could choose its own
        // reviewers (ADR-035 decision 4). Read through the same extraction the search index
        // uses, so there is one decoder rather than two that can disagree.
        var indexed = SearchableContent.Of(document.Bundle, authority);

        var transition = await lifecycle.TransitionAsync(
            new VersionRef(id, version), body.Action, subject.Id, body.Reason,
            body.SignatureReference,
            Pinning.ContextFor(
                document, conformance, authority,
                await terminology.BindingsAsync(cancellationToken)),
            new RoutingSubject(indexed.DocumentType, indexed.Scope.Market),
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
    string? productIdentifier,
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
                text, product, productIdentifier, market, language, state, identifier,
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
            body.SignatureReference, body.EffectiveFrom, cancellationToken);

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
                    new CrossReferenceCheckingContentStore(
                        new ValidatingContentStore(
                            new ScopedContentStore(
                                new RegisteringContentStore(store, lifecycle, subject.Id),
                                policy, subject),
                            validator)),
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

            // The terminology in force at approval (ADR-036 decision 3). Reported here because
            // this endpoint answers "what was this approved against", and omitting terminology
            // made that an incomplete answer to the question ADR-023 exists for. Empty means
            // the approval was asked and had none, which is not the same as never asked.
            terminologyBindings = pinned.TerminologyBindings.Select(binding => new
            {
                system = binding.System,
                version = binding.Version,
                isVersioned = binding.IsVersioned,
            }),
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

app.MapGet("/tasks", async (
    ClaimsPrincipal principal,
    IWorkflowStore tasks,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    // What is waiting for the caller: the tasks held by a role they hold, or by them
    // personally. A caller holding no roles is asked nothing, which is what an empty set of
    // assignees has to mean rather than "everything" (ADR-031 decision 4).
    var open = await tasks.OpenForAsync([.. subject.Roles, subject.Id], cancellationToken);

    return Results.Ok(open.Select(task => new
    {
        identifier = task.Identifier,
        documentIdentifier = task.Version.DocumentIdentifier,
        version = task.Version.Version,
        action = task.Action,
        assignee = task.Assignee,
        raisedAt = task.RaisedAt,
    }));
}).RequireAuthorization();

app.MapPost("/tasks/{identifier}/assignment", async (
    string identifier,
    ReassignmentRequest body,
    ClaimsPrincipal principal,
    IWorkflowStore tasks,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(body.Assignee))
    {
        return Results.BadRequest(new
        {
            problems = new[] { "A reassignment must name who the task now sits with." },
        });
    }

    var history = await tasks.HistoryAsync(identifier, cancellationToken);
    var task = WorkflowTasks.Derive(history).FirstOrDefault();

    if (task is null)
    {
        return Results.NotFound();
    }

    if (!task.IsOpen)
    {
        // 409: the request is well formed and the platform understood it. A closed task is not
        // waiting for anyone, so moving it would record an ask nobody can answer.
        return Results.Problem(
            "That task is closed, so it cannot be reassigned.",
            statusCode: StatusCodes.Status409Conflict);
    }

    await tasks.AppendAsync(
        new TaskEvent(
            identifier, task.Version, TaskEventKind.Reassigned, task.Action, body.Assignee,
            subject.Id, DateTimeOffset.UtcNow, body.Reason),
        cancellationToken);

    return Results.Ok(new { identifier, assignee = body.Assignee });
}).RequireAuthorization();

// Registrations no content write ever followed (FN-LCM-008, ADR-025).
//
// Not scoped, and it cannot be. Scope is decided on the content (ADR-025), and an inert
// registration has none - a scoped version of this report would return nothing at all, for
// exactly the reason the registration is worth reporting. So it is a platform-wide action
// restricted by role, and the policy decision is the only control standing behind it.
app.MapGet("/admin/reconciliation/registrations", async (
    double? settleMinutes,
    ClaimsPrincipal principal,
    IPolicyDecisionPoint policy,
    ILifecycleStore lifecycle,
    IContentStore content,
    IdentifierAuthority authority,
    TimeProvider clock,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    // No affiliate and no market on the resource, because the records this reports have
    // neither. The policy answers on the role alone for platform-wide actions.
    var decision = await policy.DecideAsync(
        new AuthorizationQuery(subject, "reconcile", ResourceScope.PlatformWide),
        cancellationToken);

    if (!decision.Allowed)
    {
        return Results.Problem(
            $"Access denied for action 'reconcile': {decision.Reason}",
            statusCode: StatusCodes.Status403Forbidden);
    }

    // Fifteen minutes: long enough that no content write is still in flight, short enough
    // that a failure this morning shows up this morning. Overridable per call, because the
    // right answer differs between a quick check and a scheduled sweep.
    var settle = TimeSpan.FromMinutes(settleMinutes ?? 15);
    if (settle <= TimeSpan.Zero)
    {
        return Results.BadRequest(new
        {
            problems = new[]
            {
                "A settle period must be greater than zero. A content write happens moments "
                + "after its registration, so zero reports every write in flight and this "
                + "report cannot tell those from the ones that failed.",
            },
        });
    }

    var report = await new InertRegistrationReport(
            lifecycle, content, authority.DocumentSystem, clock)
        .RunAsync(settle, cancellationToken);

    return Results.Ok(new
    {
        ranAt = report.RanAt,

        // Echoed, because a count of inert registrations means nothing without it: the same
        // platform reports differently at fifteen minutes and at a day.
        settleMinutes = report.SettlePeriod.TotalMinutes,
        inert = report.Inert.Select(i => new
        {
            documentIdentifier = i.Version.DocumentIdentifier,
            version = i.Version.Version,
            author = i.Author,
            registeredAt = i.RegisteredAt,
            blocksVersionNumber = i.BlocksVersionNumber,
        }),
    });
}).RequireAuthorization();

// A version as sections, and the way back (ADR-038, FN-CC-010).
//
// The gap ADR-037 decision 7 predicted the authoring surface would find: the surface must never
// see a Bundle, and the only read path returned one. Derived on every read and stored nowhere -
// FHIR remains the single source of truth, so there is nothing here that can come to disagree
// with it.
app.MapGet("/labels/{id}/versions/{version:int}/sections", async (
    string id,
    int version,
    ClaimsPrincipal principal,
    IContentStore store,
    IPolicyDecisionPoint policy,
    LifecycleService lifecycle,
    IdentifierAuthority authority,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    // Read through scope, so a document the caller may not see is not found rather than
    // forbidden - otherwise this endpoint would prove that a document exists (CAP-SCH-004).
    var scoped = new ScopedContentStore(store, policy, subject);
    var identity = new DocumentIdentity(authority.DocumentSystem, id);

    EpiDocument? document;
    try
    {
        document = await scoped.GetAsync(identity, version, cancellationToken);
    }
    catch (AccessDeniedException)
    {
        return Results.NotFound();
    }

    if (document is null)
    {
        return Results.NotFound();
    }

    // Whether this caller may write to this document at all, which is a scope and policy
    // question and not the state of the version in front of them (ADR-038 decision 6). Every
    // version is immutable; saving mints the next one, and drafting from an approved version is
    // how a label evolves rather than an exception to immutability.
    var scope = ContentScope.Of(document.Bundle, authority)!;
    var mayAuthor = await policy.DecideAsync(
        new AuthorizationQuery(subject, "author", new ResourceScope(scope.Affiliate, scope.Market)),
        cancellationToken);

    var currentState =
        await lifecycle.CurrentStateAsync(new VersionRef(id, version), cancellationToken)
        ?? "unknown";

    return Results.Ok(new
    {
        documentIdentifier = id,
        version,
        state = currentState,
        editable = mayAuthor.Allowed,

        // Which product the label is about, where it names one resolvably (ADR-040). Null where
        // it does not, which the surface has to be able to tell from a product it was not shown.
        product = ProductReference.Of(document.Bundle, authority) is { } named
            ? new { identifier = named.Identifier, display = named.Display }
            : null,

        // What the state model permits from here, and which of those are signed gates. Said by
        // the platform because deriving it in a browser would be a second implementation of the
        // state model, and the weaker of the two (ADR-037 decision 1). It is what the surface
        // offers; it is not what decides - every one is checked again on the way in.
        actions = labelModel.Value.Transitions
            .Where(t => string.Equals(t.From, currentState, StringComparison.Ordinal))
            .Select(t => t.Action),
        signedActions = labelModel.Value.Transitions
            .Where(t => string.Equals(t.From, currentState, StringComparison.Ordinal)
                        && t.RequiresSignature)
            .Select(t => t.Action),

        // What a signature at each signed gate must assert. Said by the platform because a
        // signature that says the wrong thing is worse than none: the gate refuses it, and the
        // record would have asserted something nobody intended (ADR-020). A surface choosing
        // its own meaning happens to work until a deployment configures a different one.
        signatureMeanings = labelModel.Value.Transitions
            .Where(t => string.Equals(t.From, currentState, StringComparison.Ordinal)
                        && t.RequiresSignature && t.SignatureMeaning is not null)
            .ToDictionary(t => t.Action, t => t.SignatureMeaning!),
        sections = SectionProjection.Of(document.Bundle).Select(section => new
        {
            identity = section.Identity,
            title = section.Title,
            narrative = section.Narrative,
        }),
    });
}).RequireAuthorization();

// Saving edited sections, which mints the next version rather than changing this one.
//
// The Bundle is assembled by patching the version that was read, never rebuilt from the
// sections (ADR-038 decision 2): a projection carries what an author may change and a Bundle
// carries a great deal more. It then goes through exactly the same write pipeline as any other
// version, so nothing about the gate is special-cased for authoring.
app.MapPost("/labels/{id}/versions/{version:int}/sections", async (
    string id,
    int version,
    SectionSaveRequest body,
    ClaimsPrincipal principal,
    IContentStore store,
    IPolicyDecisionPoint policy,
    StructuralValidator validator,
    LifecycleService lifecycle,
    IAuditSink audit,
    IEventPublisher events,
    IdentifierAuthority authority,
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    var scoped = new ScopedContentStore(store, policy, subject);
    var identity = new DocumentIdentity(authority.DocumentSystem, id);

    try
    {
        var source = await scoped.GetAsync(identity, version, cancellationToken);
        if (source is null)
        {
            return Results.NotFound();
        }

        var edited = SectionProjection.Apply(
            source.Bundle,
            [.. (body.Sections ?? []).Select(s =>
                new ProjectedSection(s.Identity ?? string.Empty, s.Title, s.Narrative ?? string.Empty))]);

        // Omission is not removal. A save that did not mention a product leaves the one that was
        // there, or this would silently detach every label a section edit touched from its
        // product (ADR-040).
        if (body.Product is { } chosen)
        {
            edited = ProductReference.Stamp(
                edited, new ProductReference(chosen.Identifier ?? string.Empty, chosen.Display),
                authority);
        }

        var next = (await scoped.VersionsAsync(identity, cancellationToken))[^1] + 1;

        var gated = new AuditingContentStore(
            new PublishingContentStore(
                new MaterialisingContentStore(
                    new CrossReferenceCheckingContentStore(
                        new ValidatingContentStore(
                            new ScopedContentStore(
                                new RegisteringContentStore(store, lifecycle, subject.Id),
                                policy, subject),
                            validator)),
                    new ScopedContentStore(store, policy, subject),
                    authority),
                events),
            audit,
            subject.Id);

        var stored = await gated.CreateVersionAsync(identity, next, edited, cancellationToken);

        return Results.Created(
            $"/labels/{id}/versions/{stored.Version}/sections",
            new { documentIdentifier = id, version = stored.Version });
    }
    catch (ArgumentException invalid)
    {
        // A section identity the version does not have. Adding a section is a separate
        // operation with its own rules (ADR-038 decision 4).
        return Results.BadRequest(new { problems = new[] { invalid.Message } });
    }
    catch (ContentRejectedException rejected)
    {
        return Results.BadRequest(new
        {
            problems = rejected.Issues.Select(issue => $"{issue.Location}: {issue.Message}"),
        });
    }
    catch (AccessDeniedException denied)
    {
        return Results.Problem(denied.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (VersionConflictException conflict)
    {
        return Results.Problem(conflict.Message, statusCode: StatusCodes.Status409Conflict);
    }
}).RequireAuthorization();

// The product directory, which ADR-036 built a port for and nothing could ask (FN-MDM-002).
//
// What the authoring surface needs to honour ADR-037 decision 3: an author picks a product and
// the platform writes its identity, rather than anybody typing an identifier.
app.MapGet("/master-data/products", async (
    string? text,
    ClaimsPrincipal principal,
    IProductDirectory products,
    CancellationToken cancellationToken) =>
{
    if (SubjectFactory.From(principal) is null)
    {
        return Results.Unauthorized();
    }

    if (string.IsNullOrWhiteSpace(text))
    {
        // A directory that answered everything to an empty query would be a way of enumerating
        // the product catalogue. A picker needs to narrow, not to list.
        return Results.BadRequest(new
        {
            problems = new[] { "Say what to search for. This does not list every product." },
        });
    }

    var found = await products.SearchAsync(text, cancellationToken);

    return Results.Ok(found.Select(product => new
    {
        identifier = product.Identifier,
        name = product.Name,
        marketingAuthorisationHolder = product.MarketingAuthorisationHolder,
        markets = product.Markets,
    }));
}).RequireAuthorization();

// The leaflet a version produces, for an author to look at (FN-RND-003).
//
// A preview, and deliberately not an official render. A render template is content that
// somebody approves (ADR-033 decision 2), there is no template store yet, and a render made
// with a template nobody approved cannot be the artefact filed with a regulator - so this is
// marked as a preview, is not written to the asset store, and says so in the output.
app.MapGet("/labels/{id}/versions/{version:int}/preview", async (
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

    var scoped = new ScopedContentStore(store, policy, subject);
    var identity = new DocumentIdentity(authority.DocumentSystem, id);

    EpiDocument? document;
    try
    {
        document = await scoped.GetAsync(identity, version, cancellationToken);
    }
    catch (AccessDeniedException)
    {
        return Results.NotFound();
    }

    if (document is null)
    {
        return Results.NotFound();
    }

    // Draft, always. The flag is what keeps an author preview distinguishable from an official
    // render (CAP-RND-004), and nothing here could produce the latter.
    var rendered = HtmlRenderer.Render(document, PreviewTemplate.Scaffold, draft: true);

    return Results.Text(
        Encoding.UTF8.GetString(rendered.Content), "text/html", Encoding.UTF8);
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
    productIdentifier = hit.ProductIdentifier,
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
        IdentifierAuthority authority,
        IReadOnlyList<TerminologyBindingRef> terminology) =>
        new(ContentHash.Of(document.Bundle),
            [.. conformance.Value.Packages.Select(p => new PinnedPackage(p.Name, p.Version, p.Sha256))],
            authority.DocumentSystem,
            TemplateInstantiation.TemplateOf(document.Bundle, authority),
            TemplateInstantiation.TemplateVersionOf(document.Bundle, authority),

            // Which terminology answered, recorded separately from what validated the structure
            // even though both come from the pinned packages today. They are different claims,
            // and a pin written now still means what it says once they diverge (ADR-036).
            [.. terminology.Select(b => new TerminologyBinding(b.System, b.Version))]);
}

/// <summary>
/// The render template a preview is made with, which is scaffolding and says so.
/// </summary>
/// <remarks>
/// ADR-033 decision 2 says render templates are content: versioned, immutable per version,
/// approved by a regulatory owner, because a template determines what a patient reads. There is
/// no template store yet, so this is not one - and it is deliberately not under `config/`
/// either, because putting it there would say an administrator may edit what a patient reads,
/// which is exactly what that decision rejects.
///
/// It exists so an author can see the shape of their content. Everything rendered with it is a
/// preview, and the day a template store exists this goes.
/// </remarks>
internal static class PreviewTemplate
{
    public static RenderTemplate Scaffold { get; } = new(
        "preview-scaffold",
        0,
        "Preview (not an approved template)",
        "body { font-family: system-ui, sans-serif; max-width: 40rem; margin: 2rem auto; }");
}

/// <summary>What a caller sends when saving edited sections (ADR-038).</summary>
/// <param name="Product">
/// Which product the label is about, where the caller is changing it. Absent means unchanged,
/// never removed: a surface saving sections without mentioning a product would otherwise detach
/// the label from its product every time somebody edited a sentence (ADR-040).
/// </param>
internal sealed record SectionSaveRequest(
    IReadOnlyList<SectionSave>? Sections, ProductSave? Product = null);

/// <summary>Which product a save says the label is about.</summary>
internal sealed record ProductSave(string? Identifier, string? Display);

/// <param name="Identity">
/// The identity the platform assigned. Named rather than positional, because a save that
/// matched sections by order would rewrite the wrong one the moment the set changed.
/// </param>
internal sealed record SectionSave(string? Identity, string? Title, string? Narrative);

/// <summary>What a caller asks for when moving a task to somebody else.</summary>
internal sealed record ReassignmentRequest(string Assignee, string? Reason);

/// <summary>What a caller asks for when signing. The signer is the token, never the body.</summary>
internal sealed record SignatureRequest(
    string DocumentIdentifier, int Version, string Meaning, string Password, string? Reason);

/// <summary>What a caller asks for when moving a version between states.</summary>
/// <param name="EffectiveFrom">
/// When a market's approval takes effect. Required on a transition that records one, refused on
/// any other, and never defaulted (ADR-029 decision 3).
/// </param>
internal sealed record TransitionRequest(
    string Action, string? Reason, string? SignatureReference, DateTimeOffset? EffectiveFrom = null);

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
