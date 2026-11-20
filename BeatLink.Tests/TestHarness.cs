using System.Reflection;

namespace BeatLink.Tests;

/// <summary>Marks a parameterless method as a test case.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute
{
    public TestAttribute(string name) => Name = name;
    public string Name { get; }
}

/// <summary>Minimal assertions. Throw <see cref="AssertException"/> on failure.</summary>
public static class Assert
{
    public static void True(bool condition, string message = "expected true")
    {
        if (!condition) throw new AssertException(message);
    }

    public static void False(bool condition, string message = "expected false") => True(!condition, message);

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertException($"expected [{expected}] but got [{actual}]");
    }

    public static void BytesEqual(byte[] expected, byte[] actual)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
            throw new AssertException(
                $"bytes differ:\n  expected {Convert.ToHexString(expected)}\n  actual   {Convert.ToHexString(actual)}");
    }

    public static void Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        catch (Exception ex) { throw new AssertException($"expected {typeof(TException).Name} but got {ex.GetType().Name}"); }
        throw new AssertException($"expected {typeof(TException).Name} but no exception was thrown");
    }
}

public sealed class AssertException : Exception
{
    public AssertException(string message) : base(message) { }
}

public static class TestHarness
{
    public static int Run()
    {
        MethodInfo[] tests = Assembly.GetExecutingAssembly()
            .GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static))
            .Where(m => m.GetCustomAttribute<TestAttribute>() is not null)
            .ToArray();

        int passed = 0, failed = 0;
        foreach (MethodInfo test in tests)
        {
            string name = test.GetCustomAttribute<TestAttribute>()!.Name;
            try
            {
                test.Invoke(null, null);
                Console.WriteLine($"  PASS  {name}");
                passed++;
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException { InnerException: { } e } ? e : ex;
                Console.WriteLine($"  FAIL  {name}\n        {inner.Message}");
                failed++;
            }
        }

        Console.WriteLine($"\n{passed} passed, {failed} failed, {tests.Length} total.");
        return failed == 0 ? 0 : 1;
    }
}
