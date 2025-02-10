using System.Runtime.CompilerServices;
using BaseLibrary;

namespace SourceGenBugTest;

internal sealed class WorkingClass : BaseClass
{
    protected override async Task DoSomethingAsync(long id, [CallerFilePath]string? filePath = null, [CallerLineNumber]int lineNumber = 0)
    {
        await base.DoSomethingAsync(id, filePath, lineNumber);
    }
}