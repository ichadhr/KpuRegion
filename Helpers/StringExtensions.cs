using System.Text;
using System.Text.RegularExpressions;

namespace kpu.Helpers
{
    public static partial class StringExtensions
    {
        [GeneratedRegex(@"^M{0,3}(CM|CD|D?C{0,3})(XC|XL|L?X{0,3})(IX|IV|V?I{0,3})$", RegexOptions.IgnoreCase)]
        private static partial Regex RomanNumeralRegex();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhitespaceRegex();

        [GeneratedRegex(@"[^\w\s'-]")]
        private static partial Regex SpecialCharacterRegex();

        public static string CapitalizeExceptRoman(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            input = input.Normalize(NormalizationForm.FormD);
            input = SpecialCharacterRegex().Replace(input, "");
            input = WhitespaceRegex().Replace(input.Trim(), " ");

            string[] words = input.Split(' ');

            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i].Trim();
                if (!string.IsNullOrEmpty(word) && !RomanNumeralRegex().IsMatch(word))
                {
                    words[i] = char.ToUpper(word[0]) + word[1..];
                }
            }

            return string.Join(" ", words);
        }
    }
}