namespace ReleaseGen.Tests;

/// <summary>
/// Expected-exception capture for the house error style: the ReleaseGen surface reports failures
/// as messages naming the failing thing, so tests capture the exception and assert on the
/// message text rather than on the throw site.
/// </summary>
internal static class Capture
{
    /// <summary>
    /// Runs <paramref name="action"/> and returns the <typeparamref name="TException"/> it threw.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="action">The code under test.</param>
    /// <returns>The caught exception.</returns>
    /// <exception cref="InvalidOperationException">The expected exception was not thrown.</exception>
    public static TException Exception<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name} but the action threw {ex.GetType().Name}: {ex.Message}",
                ex);
        }

        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name} but the action completed without throwing.");
    }

    /// <summary>
    /// Awaits <paramref name="action"/> and returns the <typeparamref name="TException"/> it threw.
    /// </summary>
    /// <typeparam name="TException">The expected exception type.</typeparam>
    /// <param name="action">The async code under test.</param>
    /// <returns>The caught exception.</returns>
    /// <exception cref="InvalidOperationException">The expected exception was not thrown.</exception>
    public static async Task<TException> ExceptionAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name} but the action threw {ex.GetType().Name}: {ex.Message}",
                ex);
        }

        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name} but the action completed without throwing.");
    }
}
