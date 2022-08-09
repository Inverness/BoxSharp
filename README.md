# Summary

BoxSharp is a proof-of-concept for in-process sandboxed C# script execution.

This is implemented using two main components:
- Compile time checking of symbol usage against a whitelist
- Runtime checking of stack size, time usage, and allocation counts

The compile time whitelist checking is the most essential security component as it limits what symbols can be used to those that are
safe or can be secured. It's not recommended to whitelist any symbols that allow access to things like files. Instead, new API's should
be created and provided to the sandbox so that these resources can be safely accessed.

The runtime checking helps prevent some blatant abuses such as causing a stack overflow, out of memory exception, or wasting large
amounts of CPU time. These issues threaten the integrity of the process running the script but are not as much of a security threat as
a script being able to use symbols it shouldn't.

# Credits

This library was inspired by and uses code from two other libraries:
- Unbreakable, by Andrey Shchekin (https://github.com/ashmind/Unbreakable)
- Banned API Analyzers, by Microsoft (https://github.com/dotnet/roslyn-analyzers/tree/main/src/Microsoft.CodeAnalysis.BannedApiAnalyzers)