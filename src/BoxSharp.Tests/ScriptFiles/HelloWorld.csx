#load ".meta/globals.csx"
#load "HelloWorldLib.csx"

string text = IsUnitTest ? "Hello world from unit test!" : "Hello world!";

WriteHeaderLine(text);
