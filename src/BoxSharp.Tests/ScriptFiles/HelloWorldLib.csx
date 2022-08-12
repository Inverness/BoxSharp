#load ".meta/globals.csx"

public static void WriteHeaderLine(string text)
{
    if (string.IsNullOrEmpty(text))
        return;

    int s = text.Length;

    var h = new string('-', s);

    System.Console.WriteLine(h);
    System.Console.WriteLine(text);
    System.Console.WriteLine(h);
}
