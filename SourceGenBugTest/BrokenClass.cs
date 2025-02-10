using System.Runtime.CompilerServices;
using BaseLibrary;

namespace SourceGenBugTest;

// This class will break with the following error on build:
// 2>BrokenClass_Overrides.g.cs(10,34): Error CS0161 : 'BrokenClass.DoSomethingAsync(long, string?, int)': not all code paths return a value
[GenerateOverride]
internal sealed partial class BrokenClass : BaseClass
{
}