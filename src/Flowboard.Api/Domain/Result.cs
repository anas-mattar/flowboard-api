// Shared Result<T> -> IResult mapping (plan.md ADR-5, fulfilling 001 ADR-1's deferred item).
namespace Flowboard.Api.Domain;

public enum FailureKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
}

public sealed class Failure
{
    private Failure(FailureKind kind, string title, Dictionary<string, string[]>? errors)
    {
        Kind = kind;
        Title = title;
        Errors = errors;
    }

    public FailureKind Kind { get; }

    public string Title { get; }

    public Dictionary<string, string[]>? Errors { get; }

    public static Failure Validation(string title, Dictionary<string, string[]>? errors = null) =>
        new(FailureKind.Validation, title, errors);

    public static Failure Unauthorized(string title = "Invalid email or password") =>
        new(FailureKind.Unauthorized, title, null);

    public static Failure Forbidden(string title = "You do not have permission to do this") =>
        new(FailureKind.Forbidden, title, null);

    public static Failure NotFound(string title = "Not found") =>
        new(FailureKind.NotFound, title, null);

    public static Failure Conflict(string title) =>
        new(FailureKind.Conflict, title, null);
}

/// <summary>Marker type for a Result with no success value (e.g. a 204 No Content action).</summary>
public readonly struct Unit
{
    public static readonly Unit Value = default;
}

public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
        Failure = null;
    }

    private Result(Failure failure)
    {
        _value = default;
        IsSuccess = false;
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public Failure? Failure { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Result has no value; check IsSuccess first.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Fail(Failure failure) => new(failure);

    public static implicit operator Result<T>(Failure failure) => new(failure);
}
