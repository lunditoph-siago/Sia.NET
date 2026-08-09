namespace Sia_BrowserTest.Acceptance;

public static class AcceptanceAssert
{
    public static void True(bool condition, string message)
    {
        if (!condition) {
            throw new AcceptanceException(message);
        }
    }

    public static void False(bool condition, string message)
        => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) {
            throw new AcceptanceException(
                message ?? $"Expected '{expected}', received '{actual}'.");
        }
    }

    public static void SequenceEqual<T>(
        IEnumerable<T> expected,
        IEnumerable<T> actual,
        string? message = null)
    {
        if (!expected.SequenceEqual(actual)) {
            throw new AcceptanceException(
                message ?? "The sequences contain different values.");
        }
    }

    public static void Contains(string expected, string actual, string? message = null)
    {
        if (!actual.Contains(expected, StringComparison.Ordinal)) {
            throw new AcceptanceException(
                message ?? $"Expected text to contain '{expected}'.");
        }
    }
}
