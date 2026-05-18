namespace Aquarium.Engine.Fractal.Tests;

public sealed class ZyphosNamingTests
{
    [Fact]
    public void ZyphosSourceDoesNotModelUmbrosAsMoon()
    {
        var root = FindRepositoryRoot();
        var zyphosFiles = Directory.GetFiles(Path.Combine(root, "src", "Aquarium.Zyphos"), "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".hlsl", StringComparison.Ordinal))
            .ToArray();

        foreach (var file in zyphosFiles)
        {
            var text = File.ReadAllText(file);
            Assert.DoesNotContain("Moon", text, StringComparison.Ordinal);
            Assert.DoesNotContain("moon", text, StringComparison.Ordinal);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Aquarium.Engine.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find Aquarium.Engine.sln.");
    }
}
