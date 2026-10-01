namespace AuditLogService.Infrastructure.Persistence;

public sealed class ChainMetadata
{
    public const int GlobalChainId = 1;

    public int ChainId { get; set; } = GlobalChainId;

    public long HeadSequenceNumber { get; set; }

    public required string HeadHash { get; set; }
}
