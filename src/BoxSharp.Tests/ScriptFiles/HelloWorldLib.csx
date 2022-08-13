#load ".meta/globals.csx"

public static void WriteHeaderLine(string text)
{
    if (string.IsNullOrEmpty(text))
        return;

    int s = text.Length;

    var h = new string('-', s);

    WriteLine(h);
    WriteLine(text);
    WriteLine(h);
}
