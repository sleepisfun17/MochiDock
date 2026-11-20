// The tests run on Windows only and link app files that use Windows APIs (DPAPI, the registry). This project targets plain
// net8.0, so without this the platform analyzer (CA1416) warns at every such call.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]
