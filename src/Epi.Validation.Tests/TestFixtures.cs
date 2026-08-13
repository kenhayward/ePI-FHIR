namespace Epi.Validation.Tests;

/// <summary>
/// Locates the shared fixtures under tests/ - the repository's home for sample ePI bundles
/// and conformance fixtures, rather than copies in each project's build output.
/// </summary>
internal static class TestFixtures
{
    public static string Path(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(System.IO.Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root (no EpiPlatform.sln found above the test output).");
        }

        return System.IO.Path.Combine([directory.FullName, "tests", "fixtures", .. segments]);
    }
}
