using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Verifier =
    Microsoft.CodeAnalysis.CSharp.Testing.XUnit.AnalyzerVerifier<
        Ryujinx.Analyzers.CatchClauseAnalyzer>;

namespace Ryujinx.Tests.Analyzers
{
    public class CatchClauseAnalyzerTests
    {
        private static readonly string _loggerTextPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            "Fixtures", "CatchClauseLogger.cs");

        private static readonly string _loggerText = File.ReadAllText(_loggerTextPath);

        [Fact]
        public async Task CatchWithoutDeclaration_WarningDiagnostic()
        {
            const string text = @"
using System;

public class MyClass
{
    public void MyMethod1()
    {
        try
        {
            Console.WriteLine(""test"");
        }
        catch
        {
            throw;
        }
    }
}
";

            var expected = Verifier.Diagnostic()
                .WithSpan(12, 9, 15, 10)
                .WithArguments("Exception");
            await Verifier.VerifyAnalyzerAsync(text, expected).ConfigureAwait(false);
        }

        [Fact]
        public async Task CatchWithoutIdentifier_WarningDiagnostic()
        {
            const string text = @"
using System;

public class MyClass
{
    public void MyMethod2()
    {
        try
        {
            Console.WriteLine(""test"");
        }
        catch (NullReferenceException)
        {
            // testme
        }
    }
}
";

            var expected = Verifier.Diagnostic()
                .WithSpan(12, 9, 15, 10)
                .WithArguments("NullReferenceException");
            await Verifier.VerifyAnalyzerAsync(text, expected).ConfigureAwait(false);
        }

        [Fact]
        public async Task LogWithoutCatchIdentifier_WarningDiagnostic()
        {
            string text = _loggerText + @"
public class MyClass
{
    public void MyMethod3()
    {
        try
        {
            Console.WriteLine(""test"");
        }
        catch (ArgumentException exception)
        {
            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.Application, ""test exception"");
        }
    }
}
";

            var expected = Verifier.Diagnostic()
                .WithSpan(89, 9, 92, 10)
                .WithArguments("ArgumentException");
            await Verifier.VerifyAnalyzerAsync(text, expected).ConfigureAwait(false);
        }

        [Fact]
        public async Task LogWithIdentifierInString_NoDiagnostic()
        {
            string text = _loggerText + @"
public class MyClass
{
    public void MyMethod4()
    {
        try
        {
            Console.WriteLine(""test"");
        }
        catch (Exception abc)
        {
            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.Application, $""test: {abc}"");
            Console.WriteLine(""Test"");
        }
    }
}
";

            await Verifier.VerifyAnalyzerAsync(text).ConfigureAwait(false);
        }

        [Fact]
        public async Task LogWithIdentifierAsArg_NoDiagnostic()
        {
            string text = _loggerText + @"
public class MyClass
{
    public void MyMethod5()
    {
        try
        {
            Console.WriteLine(""test"");
        }
        catch (System.Exception exception)
        {
            Ryujinx.Common.Logging.Logger.Info?.Print(Ryujinx.Common.Logging.LogClass.Application, $""test"", exception);
            Console.WriteLine(""Test"");
        }
    }
}
";

            await Verifier.VerifyAnalyzerAsync(text).ConfigureAwait(false);
        }
    }
}
