# Submission validation — integration checklist

One new, self-contained file: `src/Platform.Application/Forms/SubmissionValueValidator.cs`.
No new dependencies, no DI registration needed - it's a static class.

## Replace the existing required-only check in `SubmitFormDataCommand.cs`

Currently the handler only checks for missing required fields:

```csharp
var missingRequired = activeFields
    .Where(f => f.IsRequired)
    .Where(f => !request.Values.ContainsKey(f.Code) || request.Values[f.Code] is null)
    .Select(f => f.Code)
    .ToList();

if (missingRequired.Count != 0)
    throw new ValidationException(missingRequired.Select(code =>
        new FluentValidation.Results.ValidationFailure(code, $"'{code}' is required.")));
```

Replace that whole block with a call to the new validator, which covers required-ness
AND type/constraint checking in one pass:

```csharp
var validationErrors = SubmissionValueValidator.Validate(activeFields, request.Values);
if (validationErrors.Count != 0)
    throw new ValidationException(validationErrors.SelectMany(kv =>
        kv.Value.Select(msg => new FluentValidation.Results.ValidationFailure(kv.Key, msg))));
```

Add `using Platform.Application.Forms;` at the top if it's not already there (it should be,
given the file's own namespace is `Platform.Application.Forms.Commands.SubmitFormData`).

## One thing worth actually verifying while you're in this code, not just assuming

`DynamicDataRepository.InsertAsync` passes submitted values straight to Dapper as
parameters. Given `[FromBody] Dictionary<string, object?>` binds values as boxed
`System.Text.Json.JsonElement` (not native CLR types) - the same thing this new validator
had to handle defensively - it's worth confirming Dapper/SqlClient is actually converting
those correctly for non-string fields (Decimal, Boolean, DateTime, Lookup) rather than
something that's coincidentally worked in every test so far because the specific values
tried happened to tolerate it. This may be a complete non-issue - I genuinely don't know
without seeing it run - but it's cheap to check now while this code is already open, and
expensive to discover later as an intermittent bug on a data type nobody's tried yet.

## Verification

1. `dotnet build`. No migration needed - this only adds application-layer logic.
2. Real test, per field type: submit a request with a non-numeric string in a Decimal
   field, an invalid date string in a DateTime field, a Dropdown value that isn't one of
   its defined options, and a malformed GUID in a Lookup field - separately, not all at
   once. Confirm each comes back as a clean `400` with `errors: { "field_code": ["message"] }`
   naming the specific field, not a raw exception or generic 500.
3. Confirm the happy path (all valid values) still submits successfully - this shouldn't
   have changed behavior for correct data, only for incorrect data.
