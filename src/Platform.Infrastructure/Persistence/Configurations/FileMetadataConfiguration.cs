using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Files;

namespace Platform.Infrastructure.Persistence.Configurations;

public class FileMetadataConfiguration : IEntityTypeConfiguration<FileMetadata>
{
    public void Configure(EntityTypeBuilder<FileMetadata> builder)
    {
        builder.ToTable("FileMetadataEntries");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.FieldCode).IsRequired().HasMaxLength(63);
        builder.Property(f => f.BlobName).IsRequired().HasMaxLength(1024);
        builder.Property(f => f.OriginalFileName).IsRequired().HasMaxLength(260);
        builder.Property(f => f.ContentType).IsRequired().HasMaxLength(200);
        builder.HasIndex(f => f.RecordId);
    }
}
