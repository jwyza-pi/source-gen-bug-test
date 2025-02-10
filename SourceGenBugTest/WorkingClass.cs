using System.Runtime.CompilerServices;
using BaseLibrary;

namespace SourceGenBugTest;

// This is the exact same code that the source generator generates, however it does not fail to compile.
internal sealed class WorkingClass : BaseClass
{
    protected override async Task DoSomethingAsync(long id, [CallerFilePath]string? filePath = null, [CallerLineNumber]int lineNumber = 0)
    {
        await base.DoSomethingAsync(id, filePath, lineNumber);
    }
}