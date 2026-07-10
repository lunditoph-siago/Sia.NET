namespace Sia;

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

internal readonly record struct Outcome<TError>
    where TError : class
{
    public static Outcome<TError> Success => default;

    public TError? PrimaryError { get; }
    public ImmutableArray<TError> SecondaryErrors { get; }

    public bool IsSuccess => PrimaryError == null;

    private Outcome(
        TError primaryError,
        ImmutableArray<TError> secondaryErrors)
    {
        PrimaryError = primaryError;
        SecondaryErrors = secondaryErrors;
    }

    public static Outcome<TError> Failure(TError primaryError)
    {
        ArgumentNullException.ThrowIfNull(primaryError);
        return new(primaryError, []);
    }

    public Outcome<TError> Combine(Outcome<TError> other)
    {
        if (other.PrimaryError == null) {
            return this;
        }
        if (PrimaryError == null) {
            return other;
        }
        return new(
            PrimaryError,
            SecondaryErrors.IsDefaultOrEmpty
                ? [other.PrimaryError, .. other.SecondaryErrors]
                : SecondaryErrors.Add(other.PrimaryError)
                    .AddRange(other.SecondaryErrors));
    }
}

internal static class Outcome
{
    extension(Outcome<Exception> outcome)
    {
        public Outcome<Exception> Attempt(Action operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            try {
                operation();
                return outcome;
            }
            catch (Exception error) {
                return outcome.Combine(Outcome<Exception>.Failure(error));
            }
        }

        public void ThrowIfFailed()
        {
            if (outcome.IsSuccess) {
                return;
            }
            outcome.ThrowFailure();
        }

        [DoesNotReturn]
        public void ThrowFailure()
            => outcome.ThrowFailure<object>();

        [DoesNotReturn]
        public T ThrowFailure<T>()
        {
            var primaryError = outcome.PrimaryError
                ?? throw new InvalidOperationException(
                    "A successful outcome cannot be thrown as a failure.");
            if (outcome.SecondaryErrors.IsDefaultOrEmpty) {
                ExceptionDispatchInfo.Capture(primaryError).Throw();
            }
            throw new AggregateException(
                [primaryError, .. outcome.SecondaryErrors]);
        }
    }
}
