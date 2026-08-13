// Epi.Api - the HTTP host for the ePI platform.
//
// Iteration 1 (design/iteration-1.md) builds this out into the walking skeleton: authorise,
// validate, store canonically, audit, emit. This is the skeleton itself and carries no
// capability behaviour yet.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

// Liveness probe for the container platform, and the first end-to-end thread through the
// host. Deliberately unauthenticated and free of dependency checks: it answers "is this
// process serving?", not "is the platform well?". Readiness, which will check the FHIR
// server, database, and broker, is a separate concern for a later PR.
app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "epi-api" }));

app.Run();

// Exposed so the test host can reference the entry point generated from top-level statements.
public partial class Program;
