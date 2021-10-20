using System;
using System.Threading.Tasks;
using CommandLine;
using Microsoft.Build.Locator;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace NuOptimizer
{
    class Options
    {
        [Option("root-path", Required = true, HelpText = "Location of the codebase to process.")]
        public string RootPath { get; set; }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            MSBuildLocator.RegisterDefaults();

            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console(theme: ConsoleTheme.None)
                .CreateLogger();

            var parserResult = await Parser.Default.ParseArguments<Options>(args).WithParsedAsync(async options =>
            {
                await Task.Run(() => DoWork(options));
            });
            parserResult.WithNotParsed(_ => DisplayHelp());
        }

        static void DisplayHelp()
        {
            var helpText =
                "NuOptimizer helps in reducing the problem of exponential complexity in the NuGet's restore algorithm.\n" +
                "It analyses the transitive dependency graph of each project in the codebase and injects extra \n" +
                "<ProjectReference> and <PackageReference> items so the dependency graph becomes flat.\n" +
                "If the dependency graph changes, the NuOptimizer needs to be run again to re-generate these extra items.\n" +
                "\n" +
                "Note, that NuOptimizer can flatten dependency graphs safely only when the CPVM (Central Package Version Management) is enabled." +
                "\n";

            Console.Write(helpText);
        }

        static void DoWork(Options options)
        {
            var dependencyGraphFlattener = new DependencyGraphFlattener();
            dependencyGraphFlattener.Apply(options.RootPath);
        }
    }
}
