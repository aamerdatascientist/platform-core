using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Forms;

namespace Platform.Infrastructure.Persistence.Configurations;

public class FormDefinitionRoleConfiguration : IEntityTypeConfiguration<FormDefinitionRole>
{
    public void Configure(EntityTypeBuilder<FormDefinitionRole> builder)
    {
        builder.ToTable("FormDefinitionRoles");
        builder.HasKey(fdr => new { fdr.FormDefinitionId, fdr.RoleId });
    }
}
