using System.Runtime.CompilerServices;
using BaseLibrary;

namespace SourceGenBugTest;

[GenerateOverride]
internal sealed partial class BrokenClass : BaseClass
{
}