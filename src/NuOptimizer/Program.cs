using System.Threading.Tasks;
using CommandLine;
using Microsoft.Build.Locator;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace NuOptimizer
{
    public class Options
    {
        [Option("root-path", Required = false, Default = null, HelpText = "")]
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

            var parser = new Parser(s =>
            {
                s.AutoHelp = true;
                s.CaseSensitive = false;
                s.IgnoreUnknownArguments = false;
            });

            await Parser.Default.ParseArguments<Options>(args).WithParsedAsync(async options =>
            {
                await Task.Run(() => DoWork(options));
            });
        }

        static void DoWork(Options options)
        {
            if (options.RootPath != null)
            {
                var dependencyGraphFlattener = new DependencyGraphFlattener();
                dependencyGraphFlattener.Apply(options.RootPath);
            }
        }
    }
}
