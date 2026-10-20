using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Platform.Application.Common.Interfaces;
using Platform.Application.Identity.Commands.ChangeOwnPassword;
using Platform.Application.Identity.Commands.Login;
using Platform.Application.Identity.Commands.ResetUserPassword;
using Platform.Domain.Identity;
using Platform.Infrastructure.Persistence;
using Xunit;
using AppValidationException = Platform.Application.Common.Exceptions.ValidationException;
using UnauthorizedException = Platform.Application.Common.Exceptions.UnauthorizedException;

namespace Platform.Infrastructure.IntegrationTests.Identity;

public class PasswordCommandsTests
{
    private sealed class PlainHasher : IPasswordHasher
    {
        public string Hash(string plainTextPassword) => "hashed:" + plainTextPassword;
        public bool Verify(string plainTextPassword, string hash) => hash == "hashed:" + plainTextPassword;
    }

    private sealed class FakeJwt : IJwtTokenService
    {
        public TokenPair GenerateTokenPair(Guid userId, string email, IEnumerable<string> roles) =>
            new("access-" + Guid.NewGuid(), "refresh-" + Guid.NewGuid(), DateTime.UtcNow.AddMinutes(30));
        public string HashRefreshToken(string rawRefreshToken) => "h:" + rawRefreshToken;
    }

    private sealed class FakeCurrentUser : ICurrentUserService
    {
        public Guid? UserId { get; init; }
        public string? Email => null;
        public IReadOnlyCollection<string> Roles => Array.Empty<string>();
        public bool IsAuthenticated => UserId is not null;
    }

    private static async Task<(ApplicationDbContext Db, User User)> CreateAsync()
    {
        var db = new ApplicationDbContext(
            new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var user = User.Create("pm@example.com", "PM", "hashed:old-password-1", null);
        db.Users.Add(user);
        db.RefreshTokens.Add(RefreshToken.Create(user.Id, "h:phone", DateTime.UtcNow.AddDays(10)));
        db.RefreshTokens.Add(RefreshToken.Create(user.Id, "h:laptop", DateTime.UtcNow.AddDays(10)));
        await db.SaveChangesAsync();
        return (db, user);
    }

    [Fact]
    public async Task Login_WithAWrongPassword_IsUnauthorized_NotForbidden()
    {
        var (db, _) = await CreateAsync();
        var act = () => new LoginCommandHandler(db, new PlainHasher(), new FakeJwt())
            .Handle(new LoginCommand("pm@example.com", "wrong"), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedException>();
    }

    [Fact]
    public async Task ChangeOwnPassword_ChecksTheCurrentOne_ThenSignsOutOtherSessions()
    {
        var (db, user) = await CreateAsync();
        var handler = new ChangeOwnPasswordCommandHandler(db, new FakeCurrentUser { UserId = user.Id }, new PlainHasher(), new FakeJwt());

        var wrong = () => handler.Handle(new ChangeOwnPasswordCommand("nope", "new-password-22"), CancellationToken.None);
        await wrong.Should().ThrowAsync<AppValidationException>("a wrong current password is a 400, never a 401");

        var tokens = await handler.Handle(new ChangeOwnPasswordCommand("old-password-1", "new-password-22"), CancellationToken.None);

        user.PasswordHash.Should().Be("hashed:new-password-22");
        tokens.RefreshToken.Should().NotBeNullOrEmpty();
        var stored = await db.RefreshTokens.Where(t => t.UserId == user.Id).ToListAsync();
        stored.Where(t => t.IsActive).Select(t => t.TokenHash).Should().Equal("h:" + tokens.RefreshToken);
    }

    [Fact]
    public async Task ResetUserPassword_SetsTheNewPassword_AndRevokesEverySession()
    {
        var (db, user) = await CreateAsync();

        await new ResetUserPasswordCommandHandler(db, new PlainHasher())
            .Handle(new ResetUserPasswordCommand(user.Id, "brand-new-pass"), CancellationToken.None);

        user.PasswordHash.Should().Be("hashed:brand-new-pass");
        (await db.RefreshTokens.CountAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null)).Should().Be(0);
    }
}
