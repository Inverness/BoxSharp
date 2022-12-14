using System.Text;
using Microsoft.CodeAnalysis.Text;
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
        public async Task Compile_AllowedSymbols_Success()
        {
            //const string script = "for (int i = 0; i < 10; i++) {WriteLine($\"Loop {i}\"); var x = new int[2]; }";

            const string script = "System.Console.WriteLine(\"Success!\");";

            var ws = new WhitelistSettings();

            ws.AddSymbol(typeof(ScriptGlobals), true);
            ws.AddSymbol(typeof(Console), true);

            ws.AddReferenceByType(typeof(BoxCompilerTests));
            ws.AddReferenceByType(typeof(Console));

            var box = new BoxCompiler(ws, RuntimeGuardSettings.Default);

            ScriptCompileResult<object?> result = await box.Compile<object?>(script, typeof(ScriptGlobals));

            var globals = new ScriptGlobals(_output);

            Assert.Equal(CompileStatus.Success, result.Status);

            await result.Script!.RunAsync(globals);
        }

        [Fact]
        public async Task Compile_IllegalSymbol_Error()
        {
            //const string script = "for (int i = 0; i < 10; i++) {WriteLine($\"Loop {i}\"); var x = new int[2]; }";

            const string script = "System.Type.GetType(\"ATypeName\");";

            var ws = new WhitelistSettings();

            ws.AddSymbol(typeof(ScriptGlobals), true);

            ws.AddReferenceByType(typeof(BoxCompilerTests));

            var box = new BoxCompiler(ws, RuntimeGuardSettings.Default);

            ScriptCompileResult<object?> result = await box.Compile<object?>(script, typeof(ScriptGlobals));

            Assert.Equal(CompileStatus.Failed, result.Status);
        }

        [Fact]
        public async Task Compile_ScriptFileWithLoad_Success()
        {
            var ws = new WhitelistSettings();

            ws.AddSymbol(typeof(ScriptGlobals), true);
            ws.AddSymbol(typeof(Console), true);
            ws.AddSymbol(typeof(string), true);

            ws.AddReferenceByType(typeof(BoxCompilerTests));
            ws.AddReferenceByType(typeof(Console));

            var scriptFilesPath = Path.Combine(Directory.GetCurrentDirectory(), "ScriptFiles");

            var compiler = new BoxCompiler(ws, RuntimeGuardSettings.Default, scriptBaseDirectory: scriptFilesPath, isDebug: true);

            var result = await compiler.CompileFile<object>("ScriptFiles/HelloWorld/HelloWorld.csx", typeof(ScriptGlobals));

            Assert.Equal(CompileStatus.Success, result.Status);

            var globals = new ScriptGlobals(_output);

            await result.Script!.RunAsync(globals);
        }

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

            public bool IsUnitTest => true;
        }
    }
}
