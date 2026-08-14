namespace ClaudePlusPlus.Models;

internal sealed record AsarInspection(
    string ProductVersion,
    bool BlocksUnauthenticatedCdp,
    bool RequiresSignedCdpToken,
    bool UsesClaudeUserDataDirectory);
