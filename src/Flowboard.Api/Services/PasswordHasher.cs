// research.md R-1/R-2: BCrypt, work factor 12. The encoded hash is self-describing
// (algorithm/cost/salt) so no separate salt column is needed.
namespace Flowboard.Api.Services;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}

public sealed class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);

    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
}
