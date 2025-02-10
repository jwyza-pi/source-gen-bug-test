using System.Runtime.CompilerServices;

namespace BaseLibrary;

public abstract class BaseClass
{
    protected virtual async Task DoSomethingAsync(long id, [CallerFilePath] string? filePath = null,
        [CallerLineNumber] int lineNumber = 0)
    {
        await Task.Run(() => Task.CompletedTask);
    }
}