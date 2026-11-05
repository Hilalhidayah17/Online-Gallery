namespace AlbumApp.Core.Tests;

// Placeholder suite — real domain rule tests arrive in P1 (Core domain).
public class CoreSmokeTests
{
    [Fact]
    public void Core_Assembly_Is_Loadable()
    {
        var assembly = typeof(Core.AssemblyMarker).Assembly;

        Assert.Equal("AlbumApp.Core", assembly.GetName().Name);
    }
}
