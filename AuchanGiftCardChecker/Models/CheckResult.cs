using AuchanGiftCardChecker.Enums;

namespace AuchanGiftCardChecker.Models
{
	public class CheckResult
	{
        public BotStatus Status { get; set; } = BotStatus.None;
        public Dictionary<string, string> Captures { get; set; } = [];
    }
}