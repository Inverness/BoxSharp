using System.Text;

namespace BoxSharp.Tests
{
    [Trait("Type", "Unit")]
    public class BoxCompilerTests
    {
        [Fact]
        public async Task Compile_ValidSettings_Success()
        {
            var ws = new WhitelistSettings();

            await ws.LoadSymbolFileAsync(new MemoryStream(Encoding.UTF8.GetBytes(SimpleFile1Text)));

            ws.AddSymbol(typeof(object), true);
            ws.AddSymbol(typeof(IEnumerable<>), true);
            ws.AddSymbol(typeof(Type));
            ws.AddSymbol(typeof(Console), true);

            ws.AddSdkReference("netstandard");
            ws.AddSdkReference("System.Runtime");
            ws.AddSdkReference("System.Console");
            //ws.AddReferenceByType(typeof(object));
            //ws.AddReferenceByType(typeof(Console));

            var box = new BoxCompiler(ws, RuntimeGuardSettings.Default);

            ScriptCompileResult<object?> cr2 =
                await box.Compile<object?>(Script2);

            Assert.Equal(CompileStatus.Success, cr2.Status);

            object? res = await cr2.Script!.RunAsync();
        }

        private const string SimpleFile1Text = "T:System.Collections.Generic.List`1.*";

        private const string Script1 = "System.Console.WriteLine(\"Hello Script World!\");";

        private const string Script2 = "using System; for (int i = 0; i < 10; i++) {Console.WriteLine($\"Loop {i}\"); var x = new int[2]; }";
    }
}
