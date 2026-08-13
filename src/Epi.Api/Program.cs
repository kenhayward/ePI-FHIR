// Epi.Api - the HTTP host for the ePI platform.
//
// Iteration 1 (design/iteration-1.md) builds this out into the walking skeleton: authorise,
// validate, store canonically, audit, emit. This is the skeleton itself and carries no
// capability behaviour yet.

var builder = WebApplication.CreateBuilder(args);

var app = builder.Build();

app.Run();

// Exposed so the test host can reference the entry point generated from top-level statements.
public partial class Program;
