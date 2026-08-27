using Flowboard.Api.Services;

namespace Flowboard.Api.Tests;

public sealed class PasswordHasherTests
{
    private readonly IPasswordHasher _hasher = new PasswordHasher();

    [Fact]
    public void Hash_ThenVerify_WithCorrectPassword_Succeeds()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.True(_hasher.Verify("correct horse battery staple", hash));
    }

    [Fact]
    public void Verify_WithWrongPassword_Fails()
    {
        var hash = _hasher.Hash("correct horse battery staple");

        Assert.False(_hasher.Verify("wrong password", hash));
    }
}
