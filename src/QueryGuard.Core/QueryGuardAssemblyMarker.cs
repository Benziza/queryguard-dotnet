namespace QueryGuard;

/// <summary>
/// Anchors reflection against the QueryGuard.Core assembly without widening the public API
/// surface. Used by the build-configuration tests and by reporters that stamp the producing
/// assembly version into their output.
/// </summary>
internal static class QueryGuardAssemblyMarker
{
}
