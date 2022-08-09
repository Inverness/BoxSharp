using System.Text;
using Xunit.Abstractions;

namespace BoxSharp.Tests
{
    [Trait("Type", "Unit")]
    public class BoxCompilerTests
    {
        private readonly ITestOutputHelper _output;

        public BoxCompilerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public async Task Compile_ValidSettings_Success()
        {
            var ws = new WhitelistSettings();

            await ws.LoadSymbolFileAsync(new MemoryStream(Encoding.UTF8.GetBytes(SimpleFile1Text)));

            ws.AddSymbol(typeof(object), true);
            ws.AddSymbol(typeof(IEnumerable<>), true);
            ws.AddSymbol(typeof(Type));
            ws.AddSymbol(typeof(Console), true);
            ws.AddSymbol(typeof(ScriptGlobals), true);

            ws.AddSdkReference("netstandard");
            ws.AddSdkReference("System.Runtime");
            ws.AddSdkReference("System.Console");
            ws.AddReferenceByType(typeof(BoxCompilerTests));
            //ws.AddReferenceByType(typeof(object));
            //ws.AddReferenceByType(typeof(Console));

            var box = new BoxCompiler(ws, RuntimeGuardSettings.Default);

            ScriptCompileResult<object?> result = await box.Compile<object?>(Script2, typeof(ScriptGlobals));

            var globals = new ScriptGlobals(_output);

            Assert.Equal(CompileStatus.Success, result.Status);

            await result.Script!.RunAsync(globals);
        }

        private const string SimpleFile1Text = "T:System.Collections.Generic.List`1.*";

        private const string Script1 = "WriteLine(\"Hello Script World!\");";

        private const string Script2 = "for (int i = 0; i < 10; i++) {WriteLine($\"Loop {i}\"); var x = new int[2]; }";

        public class ScriptGlobals
        {
            private readonly ITestOutputHelper _output;

            public ScriptGlobals(ITestOutputHelper output)
            {
                _output = output;
            }

            public void WriteLine(string text)
            {
                _output.WriteLine(text);
            }
        }
    }
}
