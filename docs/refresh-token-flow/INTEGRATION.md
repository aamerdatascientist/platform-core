# Refresh-token flow — integration checklist

Two new files, both self-contained, safe to drop in as-is:
- `src/Platform.Application/Identity/Commands/RefreshToken/RefreshTokenCommand.cs`
- `src/Platform.Application/Identity/Commands/Logout/LogoutCommand.cs`

No DI changes needed - both go through `IApplicationDbContext` and the existing
`IJwtTokenService`, same as `LoginCommandHandler` already does. MediatR's assembly
scanning picks up the new handlers automatically.

## One addition to `AuthController.cs`

Add these two endpoints, alongside the existing `Register`/`Login`/`Me`:

```csharp
public record RefreshTokenRequest(string RefreshToken);

[HttpPost("refresh")]
public async Task<ActionResult<TokenPair>> Refresh(RefreshTokenRequest request, CancellationToken cancellationToken) =>
    Ok(await _sender.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken));

public record LogoutRequest(string RefreshToken);

[HttpPost("logout")]
public async Task<IActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
{
    await _sender.Send(new LogoutCommand(request.RefreshToken), cancellationToken);
    return NoContent();
}
```

Add these two usings at the top of the file:
```csharp
using Platform.Application.Identity.Commands.RefreshToken;
using Platform.Application.Identity.Commands.Logout;
```

Neither endpoint needs `[Authorize]` - same reasoning as `Login`. The refresh token
itself is the credential being presented; requiring a (possibly already-expired) access
token to call `/refresh` would defeat the entire point of the endpoint.

## Verification

1. `dotnet build` first.
2. No migration needed - `RefreshTokens` table already exists from Phase 0.
3. Real test: log in via Swagger, copy the `refreshToken` from the response (not the
   access token), call `POST /api/auth/refresh` with it. Confirm you get back a NEW
   access token AND a NEW refresh token - then try calling `/refresh` again with the
   ORIGINAL refresh token. That second call should fail (403) - that's rotation working,
   not a bug. Then test `/logout` the same way: revoke a token, confirm a subsequent
   `/refresh` with it also fails.
