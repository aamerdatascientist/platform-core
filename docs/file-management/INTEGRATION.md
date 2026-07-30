# File Management — integration checklist

New files (self-contained, safe to drop in at matching paths):
- `src/Platform.Domain/Files/FileMetadata.cs`
- `src/Platform.Application/Common/Interfaces/IBlobStorageService.cs`
- `src/Platform.Application/Files/Commands/UploadFile/UploadFileCommand.cs`
- `src/Platform.Application/Files/Commands/DeleteFile/DeleteFileCommand.cs`
- `src/Platform.Application/Files/Queries/GetFilesForRecord/GetFilesForRecordQuery.cs`
- `src/Platform.Application/Files/Queries/GetFileDownloadUrl/GetFileDownloadUrlQuery.cs`
- `src/Platform.Infrastructure/Files/BlobStorageService.cs`
- `src/Platform.Infrastructure/Persistence/Configurations/FileMetadataConfiguration.cs`
- `src/Platform.Api/Controllers/FilesController.cs`

## 1. New NuGet package

`src/Platform.Infrastructure/Platform.Infrastructure.csproj` needs `Azure.Storage.Blobs`
added to its `<ItemGroup>` of `PackageReference`s:
```xml
<PackageReference Include="Azure.Storage.Blobs" Version="12.22.2" />
```

## 2. `IApplicationDbContext.cs` and `ApplicationDbContext.cs`

Add one DbSet to each, same pattern as every other module:
```csharp
DbSet<Platform.Domain.Files.FileMetadata> FileMetadataEntries { get; }
```
(and in `ApplicationDbContext.cs`: `public DbSet<Platform.Domain.Files.FileMetadata> FileMetadataEntries => Set<Platform.Domain.Files.FileMetadata>();`)

## 3. `Platform.Infrastructure/DependencyInjection.cs`

Register the new service, alongside the existing ones:
```csharp
services.AddScoped<IBlobStorageService, BlobStorageService>();
```
Add `using Platform.Infrastructure.Files;` at the top if not already present.

## 4. Configuration

Add a `BlobStorage` connection string. This should go through `dotnet user-secrets`
locally, exactly like `DefaultConnection` - **do not** put the real value in
`appsettings.Development.json`, which is tracked by git:
```
dotnet user-secrets set "ConnectionStrings:BlobStorage" "<the connection string>"
```
`appsettings.json` at the repo root should still get an empty placeholder added for
documentation purposes, same pattern as `DefaultConnection`:
```json
"ConnectionStrings": {
  "DefaultConnection": "",
  "BlobStorage": ""
}
```

## 5. Migration

```
dotnet ef migrations add AddFileManagement --project src/Platform.Infrastructure --startup-project src/Platform.Api
dotnet ef database update --project src/Platform.Infrastructure --startup-project src/Platform.Api
```
This only adds the `FileMetadataEntries` table - no changes to any dynamic table, since
attachments deliberately never get a physical column there (see `FieldType` remarks).

## Verification

1. `dotnet build` first.
2. Confirm the `attachments` container actually exists in the real storage account and is
   set to Private access - the SAS-URL code assumes that; a misconfigured public container
   wouldn't fail loudly, it would just silently be less secure.
3. Real test: pick a form with an Attachment field (or add one via the Form Builder),
   submit a record, then `POST` a real image file to
   `/forms/{formId}/records/{recordId}/attachments` with `fieldCode` set correctly.
   Confirm `GET /records/{recordId}/attachments` lists it, `GET
   /attachments/{fileId}/download-url` returns a URL that actually opens the image in a
   browser, and that URL stops working after roughly 10 minutes (confirms the SAS
   expiry is real, not just configured and ignored).
4. Confirm uploading against a `fieldCode` that isn't a real Attachment field on that form
   correctly fails with a 400, not a 500 - this is the actual security boundary, worth
   testing deliberately, not just trusting the code review.
