namespace ClaudePlusPlus.Models;

internal sealed record ClaudeCdpRuntime(
    string Version,
    string RuntimeDirectory,
    string ExecutablePath,
    string AsarPath,
    string UserDataPath,
    string SourceAsarSha256,
    string PatchedAsarSha256);
