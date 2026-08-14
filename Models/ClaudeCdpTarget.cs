namespace ClaudePlusPlus.Models;

internal sealed record ClaudeCdpTarget(
    int Port,
    int? ProcessId,
    string Id,
    string Title,
    string Url,
    string WebSocketDebuggerUrl,
    string InspectorUrl,
    bool ReusedExistingProcess);
