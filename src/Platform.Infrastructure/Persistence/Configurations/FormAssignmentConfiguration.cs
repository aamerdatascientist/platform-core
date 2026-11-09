using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Platform.Domain.Forms;
using Platform.Domain.Identity;

namespace Platform.Infrastructure.Persistence.Configurations;

public class FormAssignmentConfiguration : IEntityTypeConfiguration<FormAssignment>
{
    public void Configure(EntityTypeBuilder<FormAssignment> builder)
    {
        builder.ToTable("FormAssignments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Note).HasMaxLength(400);

        builder.HasOne<FormDefinition>().WithMany().HasForeignKey(a => a.FormDefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);

        // "My assignments" reads by user; the tracking page by form. Not unique: a user can be
        // assigned the same form again after an earlier assignment was stopped.
        builder.HasIndex(a => new { a.UserId, a.FormDefinitionId });
        builder.HasIndex(a => a.FormDefinitionId);
    }
}
