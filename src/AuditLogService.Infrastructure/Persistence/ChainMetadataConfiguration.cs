using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AuditLogService.Infrastructure.Persistence;

public sealed class ChainMetadataConfiguration : IEntityTypeConfiguration<ChainMetadata>
{
    public void Configure(EntityTypeBuilder<ChainMetadata> builder)
    {
        builder.ToTable("chain_metadata", table =>
        {
            table.HasCheckConstraint("ck_chain_metadata_single_global_chain", "chain_id = 1");
            table.HasCheckConstraint("ck_chain_metadata_head_sequence_nonnegative", "head_sequence_number >= 0");
            table.HasCheckConstraint(
                "ck_chain_metadata_head_hash_format",
                "(head_sequence_number = 0 AND head_hash = 'GENESIS') OR " +
                "(head_sequence_number > 0 AND head_hash ~ '^[0-9a-f]{64}$')");
        });

        builder.HasKey(metadata => metadata.ChainId)
            .HasName("pk_chain_metadata");
        builder.Property(metadata => metadata.ChainId)
            .HasColumnName("chain_id")
            .ValueGeneratedNever();
        builder.Property(metadata => metadata.HeadSequenceNumber)
            .HasColumnName("head_sequence_number")
            .IsRequired();
        builder.Property(metadata => metadata.HeadHash)
            .HasColumnName("head_hash")
            .HasMaxLength(64)
            .IsRequired();
        builder.HasData(new ChainMetadata
        {
            ChainId = ChainMetadata.GlobalChainId,
            HeadSequenceNumber = 0,
            HeadHash = AuditLogService.Domain.HashChain.GenesisHash
        });
    }
}
