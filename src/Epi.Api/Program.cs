using System.Security.Claims;
using Epi.ContentCore;
using Epi.Governance.Configuration;
using Epi.Iam;
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

builder.Services.AddSingleton<IContentStore, InMemoryContentStore>();
builder.Services.AddSingleton(_ => new StructuralValidator(ProfileSource.FromPinnedPackages()));

var app = builder.Build();

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
        var gated = new ValidatingContentStore(
            new ScopedContentStore(store, policy, subject), validator);

        var stored = await gated.CreateAsync(bundle, cancellationToken);
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
    CancellationToken cancellationToken) =>
{
    var subject = SubjectFactory.From(principal);
    if (subject is null)
    {
        return Results.Unauthorized();
    }

    var scoped = new ScopedContentStore(store, policy, subject);
    var document = await scoped.GetLatestAsync(
        new DocumentIdentity(ContentCoreDefaults.DocumentIdentifierSystem, id), cancellationToken);

    // Out of scope is indistinguishable from absent, so a caller cannot learn that a document
    // they may not see exists (CAP-SCH-004).
    return document is null
        ? Results.NotFound()
        : Results.Content(EpiBundleReader.Write(document.Bundle), "application/fhir+json");
}).RequireAuthorization();

app.Run();

// Exposed so the test host can reference the entry point generated from top-level statements.
public partial class Program;
