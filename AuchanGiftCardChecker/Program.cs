using AuchanGiftCardChecker.Models;
using CommandLine;

namespace AuchanGiftCardChecker
{
    class Program
    {
        static async Task Main(string[] args)
        {
            await Parser.Default.ParseArguments<RunOptions>(args).WithParsedAsync(RunAsync);
        }

        static async Task RunAsync(RunOptions runOptions)
        {
            var checker = new Checker(runOptions);

            await checker.StartAsync();
        }
    }
}