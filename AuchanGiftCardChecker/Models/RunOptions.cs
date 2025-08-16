using CommandLine;

namespace AuchanGiftCardChecker.Models
{
    [Verb("run")]
    public class RunOptions
    {
        [Option('p', "proxy", Required = true)]
        public string Proxy { get; set; } = string.Empty;

        [Option('b', "bots", Default = 1)]
        public int Bots { get; set; }

        [Option('t', "maxCPM", Default = 300)]
        public int MaxCPM { get; set; }

        [Option('v', "verbose", Default = false)]
        public bool Verbose { get; set; }
    }
}

