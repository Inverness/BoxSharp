// This file contains global symbols that are used for code completion by IDE's.
// At runtime, any #load directives referencing the .meta folder are ignored

/// <summary>
/// Writes a line to the standard output.
/// </summary>
/// <param name="text"></param>
/// <returns></returns>
public static string WriteLine(string text) => throw Meta();

/// <summary>
/// Gets whether the current code is executing within a unit test.
/// </summary>
public static bool IsUnitTest => throw Meta();

private static System.NotImplementedException Meta() =>
    new System.NotImplementedException("Meta declaration not implemented at runtime");
