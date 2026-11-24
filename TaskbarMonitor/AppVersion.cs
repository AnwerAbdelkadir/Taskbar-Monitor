using System.Reflection;

namespace TaskbarMonitor;

/// <summary>The csproj &lt;Version&gt; at runtime, without build metadata (e.g. "1.0.0").</summary>
internal static class AppVersion
{
    public static string Display { get; } =
        (typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "1.0.0")
        .Split('+')[0];
}
