using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Exceptions;
using Platform.Application.Common.Interfaces;
using Platform.Domain.Forms;

namespace Platform.Application.Forms.Commands.FormSections;

public record FormSectionDto(Guid Id, string Code, string TitleAr, string? TitleEn, int MinBlocks, int MaxBlocks, string? ChildTableName);

internal static class SectionLoading
{
    public static async Task<FormDefinition> LoadAsync(IApplicationDbContext db, Guid formId, CancellationToken ct) =>
        await db.FormDefinitions
            .Include(f => f.Versions).ThenInclude(v => v.Fields)
            .Include(f => f.Versions).ThenInclude(v => v.Sections)
            .Include(f => f.Versions).ThenInclude(v => v.Steps)
            .SingleOrDefaultAsync(f => f.Id == formId, ct)
        ?? throw new NotFoundException(nameof(FormDefinition), formId);

    public static T Domain<T>(Func<T> action)
    {
        try { return action(); }
        catch (ArgumentException ex) { throw new Common.Exceptions.ValidationException("form.section.invalid", ex.Message); }
        catch (InvalidOperationException ex) { throw new Common.Exceptions.ValidationException("form.section.invalid", ex.Message); }
        catch (KeyNotFoundException ex) { throw new NotFoundException("Section", ex.Message); }
    }

    public static FormSectionDto ToDto(FormSection s) => new(s.Id, s.Code, s.TitleAr, s.TitleEn, s.MinBlocks, s.MaxBlocks, s.ChildTableName);
}

/// <summary>Adds a repeatable section (Safe: metadata; a live form's section gets its table with its first field).</summary>
public record AddFormSectionCommand(Guid FormDefinitionId, string Code, string TitleAr, string? TitleEn, int MinBlocks, int MaxBlocks) : IRequest<FormSectionDto>;

public class AddFormSectionCommandValidator : AbstractValidator<AddFormSectionCommand>
{
    public AddFormSectionCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(40);
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TitleEn).MaximumLength(200);
    }
}

public class AddFormSectionCommandHandler(IApplicationDbContext db) : IRequestHandler<AddFormSectionCommand, FormSectionDto>
{
    public async Task<FormSectionDto> Handle(AddFormSectionCommand request, CancellationToken cancellationToken)
    {
        var form = await SectionLoading.LoadAsync(db, request.FormDefinitionId, cancellationToken);
        var target = form.ResolveFieldEditTarget();
        if (target.Version.Fields.Any(f => f.Code == request.Code.Trim().ToLowerInvariant()))
            throw new Common.Exceptions.ValidationException("form.section.invalid", "A field already uses that code - pick another for the section.");
        var section = SectionLoading.Domain(() => target.Version.AddSection(request.Code, request.TitleAr, request.TitleEn, request.MinBlocks, request.MaxBlocks));
        db.FormSections.Add(section);
        await db.SaveChangesAsync(cancellationToken);
        return SectionLoading.ToDto(section);
    }
}

public record UpdateFormSectionCommand(Guid FormDefinitionId, Guid SectionId, string TitleAr, string? TitleEn, int MinBlocks, int MaxBlocks) : IRequest<FormSectionDto>;

public class UpdateFormSectionCommandValidator : AbstractValidator<UpdateFormSectionCommand>
{
    public UpdateFormSectionCommandValidator()
    {
        RuleFor(x => x.TitleAr).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TitleEn).MaximumLength(200);
    }
}

public class UpdateFormSectionCommandHandler(IApplicationDbContext db) : IRequestHandler<UpdateFormSectionCommand, FormSectionDto>
{
    public async Task<FormSectionDto> Handle(UpdateFormSectionCommand request, CancellationToken cancellationToken)
    {
        var form = await SectionLoading.LoadAsync(db, request.FormDefinitionId, cancellationToken);
        var section = SectionLoading.Domain(() => form.ResolveFieldEditTarget().Version.FindSectionOrThrow(request.SectionId));
        SectionLoading.Domain(() => { section.Rename(request.TitleAr, request.TitleEn); section.SetBlockLimits(request.MinBlocks, request.MaxBlocks); return 0; });
        await db.SaveChangesAsync(cancellationToken);
        return SectionLoading.ToDto(section);
    }
}

public record RemoveFormSectionCommand(Guid FormDefinitionId, Guid SectionId) : IRequest;

public class RemoveFormSectionCommandHandler(IApplicationDbContext db) : IRequestHandler<RemoveFormSectionCommand>
{
    public async Task Handle(RemoveFormSectionCommand request, CancellationToken cancellationToken)
    {
        var form = await SectionLoading.LoadAsync(db, request.FormDefinitionId, cancellationToken);
        var removed = SectionLoading.Domain(() => form.ResolveFieldEditTarget().Version.RemoveSection(request.SectionId));
        db.FormSections.Remove(removed);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Moves a field in or out of a section - only before the form is first published.</summary>
public record MoveFieldToSectionCommand(Guid FormDefinitionId, Guid FieldDefinitionId, Guid? SectionId) : IRequest;

public class MoveFieldToSectionCommandHandler(IApplicationDbContext db) : IRequestHandler<MoveFieldToSectionCommand>
{
    public async Task Handle(MoveFieldToSectionCommand request, CancellationToken cancellationToken)
    {
        var form = await SectionLoading.LoadAsync(db, request.FormDefinitionId, cancellationToken);
        var version = form.ResolveFieldEditTarget().Version;
        SectionLoading.Domain(() => { version.MoveFieldToSection(request.FieldDefinitionId, request.SectionId); return 0; });
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Formula, row filter, visibility attribute and options provider of one field (Safe: metadata only).</summary>
public record UpdateFieldSettingsCommand(
    Guid FormDefinitionId, Guid FieldDefinitionId, string? Formula, string? RowFilterJson, string? VisibleWhenAttribute, string? OptionsProvider) : IRequest;

public class UpdateFieldSettingsCommandHandler(IApplicationDbContext db, IFieldOptionsProviders providers) : IRequestHandler<UpdateFieldSettingsCommand>
{
    public async Task Handle(UpdateFieldSettingsCommand request, CancellationToken cancellationToken)
    {
        var form = await SectionLoading.LoadAsync(db, request.FormDefinitionId, cancellationToken);
        var version = form.ResolveFieldEditTarget().Version;
        var field = version.FindFieldOrThrow(request.FieldDefinitionId);
        FieldExtras.Apply(field, version, providers, request.Formula, request.RowFilterJson, request.VisibleWhenAttribute, request.OptionsProvider);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Marks a form as a system form and/or hidden from the menus (administrators).</summary>
public record SetFormFlagsCommand(Guid FormDefinitionId, bool IsSystem, bool IsHidden) : IRequest;

public class SetFormFlagsCommandHandler(IApplicationDbContext db) : IRequestHandler<SetFormFlagsCommand>
{
    public async Task Handle(SetFormFlagsCommand request, CancellationToken cancellationToken)
    {
        var form = await db.FormDefinitions.SingleOrDefaultAsync(f => f.Id == request.FormDefinitionId, cancellationToken)
            ?? throw new NotFoundException(nameof(FormDefinition), request.FormDefinitionId);
        form.SetFlags(request.IsSystem, request.IsHidden);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The registered options providers, for the builder's picker.</summary>
public record GetOptionsProvidersQuery : IRequest<IReadOnlyList<OptionsProviderDto>>;

public record OptionsProviderDto(string Key, string Description);

public class GetOptionsProvidersQueryHandler(IFieldOptionsProviders providers) : IRequestHandler<GetOptionsProvidersQuery, IReadOnlyList<OptionsProviderDto>>
{
    public Task<IReadOnlyList<OptionsProviderDto>> Handle(GetOptionsProvidersQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OptionsProviderDto>>(providers.All.Select(p => new OptionsProviderDto(p.Key, p.Description)).ToList());
}
