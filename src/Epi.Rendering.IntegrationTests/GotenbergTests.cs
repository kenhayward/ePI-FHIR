using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Rendering.IntegrationTests;

/// <summary>A real print engine, on the image the development stack pins.</summary>
/// <remarks>
/// Readiness is waited for here rather than declared with <c>WithWaitStrategy</c>. Attaching one
/// to a container built inside an xUnit collection fixture crashes the VSTest host outright -
/// every test reports failed in about a millisecond with no message and no exception - on
/// Testcontainers 4.13.0. It is not specific to the HTTP strategy; waiting on a log message
/// crashes it too, and removing the strategy fixes it. Polling after the container is up costs a
/// few lines and behaves.
/// </remarks>
public sealed class PrintEngineContainer : IAsyncLifetime
{
    /// <summary>The image the development stack runs (deploy/docker-compose).</summary>
    private const string Image = "gotenberg/gotenberg:8.35.0";

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithPortBinding(3000, assignRandomHostPort: true)
        .Build();

    public HttpClient Client { get; private set; } = new();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Client = new HttpClient
        {
            BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(3000)}/"),
        };

        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (true)
        {
            try
            {
                using var health = await Client.GetAsync("health");
                if (health.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException) when (DateTimeOffset.UtcNow < deadline)
            {
                // Still starting. Anything after the deadline is a real failure and is thrown.
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"{Image} did not become ready within two minutes.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(PrintEngineCollection.Name)]
public sealed class PrintEngineCollection : ICollectionFixture<PrintEngineContainer>
{
    public const string Name = "print-engine";
}

// Determinism against the engine that actually writes the bytes (ADR-033).
//   IT-019 A label version renders to a PDF, and two renders of it are byte-identical
//   CAP-RND-002 Render FHIR ePI to PDF, the rendered-PDF lineage
//   CAP-RND-007 Deterministic, reproducible renders
//
// Container-backed on purpose. The two dates this has to normalise are written by Chromium, and
// a stand-in that imitates it proves only that the stand-in was imitated correctly.
[Collection(PrintEngineCollection.Name)]
[Trait("Category", "Container")]
public sealed class GotenbergTests(PrintEngineContainer engine)
{
    private static readonly DateTimeOffset ContentDate = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly RenderTemplate Template =
        new("qrd-leaflet", 2, "EU QRD package leaflet", "body { font-family: sans-serif; }");

    private static EpiDocument Document() => new(
        new DocumentIdentity(
            IdentifierAuthority.Demonstration.DocumentSystem,
            "01a00000-0000-7000-8000-00000000000a"),
        3,
        new Bundle
        {
            Type = Bundle.BundleType.Document,
            Entry = [new Bundle.EntryComponent
            {
                Resource = new Composition
                {
                    Title = "SYNTHETIC TEST LABEL - Examplinum 10 mg tablets",
                    Language = "en-GB",
                    Section =
                    [
                        new Composition.SectionComponent
                        {
                            ElementId = "sec-1",
                            Title = "1. What Examplinum is and what it is used for",
                            Text = new Narrative
                            {
                                Status = Narrative.NarrativeStatus.Generated,
                                Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">"
                                      + "<p>Synthetic test content.</p></div>",
                            },
                        },
                    ],
                },
            }],
        });

    [Fact]
    public async Task IT_019_a_label_version_renders_to_a_pdf()
    {
        var rendered = await PdfRenderer.RenderAsync(
            Document(), Template, new GotenbergPrintEngine(engine.Client), ContentDate);

        Assert.Equal("application/pdf", rendered.MediaType);
        Assert.Equal("%PDF", Encoding.ASCII.GetString(rendered.Content[..4]));
        Assert.True(rendered.Content.Length > 1000, "a rendered leaflet should not be almost empty");
    }

    [Fact]
    public async Task IT_019_two_renders_of_the_same_version_are_byte_identical()
    {
        // Acceptance criterion 8, against the engine that actually writes the bytes. Without
        // normalisation these differ in exactly two bytes - the seconds in the two dates
        // Chromium writes for itself - which is the measurement recorded in ADR-033. The delay
        // is what makes the case discriminating: without it the two runs can land in the same
        // second and pass whether or not anything normalises them.
        var printer = new GotenbergPrintEngine(engine.Client);

        var first = await PdfRenderer.RenderAsync(Document(), Template, printer, ContentDate);
        await Task.Delay(TimeSpan.FromSeconds(2));
        var second = await PdfRenderer.RenderAsync(Document(), Template, printer, ContentDate);

        Assert.Equal(first.Content, second.Content);
    }

    [Fact]
    public async Task IT_019_the_pdf_carries_the_content_date_rather_than_the_moment_it_was_made()
    {
        var rendered = await PdfRenderer.RenderAsync(
            Document(), Template, new GotenbergPrintEngine(engine.Client), ContentDate);

        var text = Encoding.Latin1.GetString(rendered.Content);

        Assert.Contains("/CreationDate (D:20260301090000", text, StringComparison.Ordinal);
        Assert.DoesNotContain(
            $"/CreationDate (D:{DateTimeOffset.UtcNow:yyyyMMdd}", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IT_019_a_different_template_version_produces_a_different_pdf()
    {
        var printer = new GotenbergPrintEngine(engine.Client);

        var first = await PdfRenderer.RenderAsync(Document(), Template, printer, ContentDate);
        var second = await PdfRenderer.RenderAsync(
            Document(), Template with { Version = 3, Stylesheet = "body { color: navy; }" },
            printer, ContentDate);

        Assert.NotEqual(first.Content, second.Content);
    }

    [Fact]
    public async Task IT_019_the_engines_own_message_survives_a_refusal()
    {
        // Swallowing it would leave a render failing with a status code and nothing to act on.
        // The engine refuses a form whose file is not named index.html, and its message says so.
        var printer = new GotenbergPrintEngine(engine.Client);
        using var form = new MultipartFormDataContent { { new StringContent("<p>x</p>"), "files", "wrong.html" } };

        using var response = await engine.Client.PostAsync("forms/chromium/convert/html", form);

        Assert.False(response.IsSuccessStatusCode);
        Assert.Contains("index.html", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
