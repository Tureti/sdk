using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Proton.Sdk;

[DebuggerDisplay("{IsSome ? Value.ToString() : \"<none>\",nq}")]
public readonly struct Option<T>(T value)
{
    private readonly T _value = value;

    public static Option<T> None => default;

    public bool IsSome { get; } = true;

    public T Value => IsSome ? _value : throw new InvalidOperationException();

    public static implicit operator Option<T>(T? value) => value is null ? None : new Option<T>(value);

    public static explicit operator T(Option<T> option) => option.Value;

    public static Option<T> Some(T value) => new(value);

    public static Option<T> FromNullable(T? value) => value is not null ? new Option<T>(value) : None;

    public bool TryGetValue([NotNullWhen(true)] out T? value)
    {
        value = _value;
        return IsSome;
    }
}
