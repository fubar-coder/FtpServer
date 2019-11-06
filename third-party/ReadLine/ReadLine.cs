// <autogenerated />

using Internal.ReadLine;
using Internal.ReadLine.Abstractions;

using System.Collections.Generic;
using System.Threading.Tasks;

namespace System
{
    internal static class ReadLine
    {
        private static List<string> _history;

        static ReadLine()
        {
            _history = new List<string>();
        }

        public static void AddHistory(params string[] text) => _history.AddRange(text);
        public static List<string> GetHistory() => _history;
        public static void ClearHistory() => _history = new List<string>();
        public static bool HistoryEnabled { get; set; }
        public static IAutoCompleteHandler AutoCompletionHandler { private get; set; }

        public static async Task<string> ReadAsync(string prompt = "", string @default = "")
        {
            Console.Write(prompt);
            var keyHandler = new KeyHandler(new Console2(), _history, AutoCompletionHandler);
            var text = await GetTextAsync(keyHandler).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(@default))
            {
                text = @default;
            }
            else
            {
                if (HistoryEnabled)
                {
                    _history.Add(text);
                }
            }

            return text;
        }

        public static Task<string> ReadPasswordAsync(string prompt = "")
        {
            Console.Write(prompt);
            var keyHandler = new KeyHandler(new Console2() { PasswordMode = true }, null, null);
            return GetTextAsync(keyHandler);
        }

        private static async Task<string> GetTextAsync(KeyHandler keyHandler)
        {
            var keyInfo = Console.ReadKey(true);
            while (keyInfo.Key != ConsoleKey.Enter)
            {
                await keyHandler.Handle(keyInfo).ConfigureAwait(false);
                keyInfo = Console.ReadKey(true);
            }

            Console.WriteLine();
            return keyHandler.Text;
        }
    }
}