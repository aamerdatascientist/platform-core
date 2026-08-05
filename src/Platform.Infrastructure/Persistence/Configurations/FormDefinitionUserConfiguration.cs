using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Forms;

namespace Platform.Infrastructure.Persistence.Configurations;

public class FormDefinitionUserConfiguration : IEntityTypeConfiguration<FormDefinitionUser>
{
    public void Configure(EntityTypeBuilder<FormDefinitionUser> builder)
    {
        builder.ToTable("FormDefinitionUsers");
        builder.HasKey(fdu => new { fdu.FormDefinitionId, fdu.UserId });
    }
}
