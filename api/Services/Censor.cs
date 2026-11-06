using System.Text;
using System.Text.RegularExpressions;

namespace api.Services;

// Masks slurs with asterisks of the same length, on write. Matches whole words only (so "raccoon" is safe), any case, with
// repeated letters ("niggger") and common look-alikes (n1gger, f@ggot). A trailing "s" is covered ("kikes").
// ponytail: no separators between letters (n.i.g.g.e.r) and no other alphabets; add when it shows up in real text.
public static class Censor
{
    static readonly Dictionary<char, string> Alike = new()
    {
        ['a'] = "a@4", ['e'] = "e3", ['i'] = "i1!|", ['o'] = "o0", ['s'] = "s$5", ['t'] = "t7", ['g'] = "g9", ['l'] = "l1",
    };

    static readonly Regex Slurs = new(
        @"(?<![\p{L}\p{N}])(?:" + string.Join("|", SlurList.Words.Select(Pattern)) + @")s?(?![\p{L}\p{N}])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // each distinct letter becomes a class that repeats; "gg" in the word collapses so "g+" covers any number of them
    static string Pattern(string word)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < word.Length; i++)
        {
            if (i > 0 && word[i] == word[i - 1]) continue;
            sb.Append('[').Append(Regex.Escape(Alike.GetValueOrDefault(word[i], word[i].ToString()))).Append("]+");
        }
        return sb.ToString();
    }

    public static string Mask(string text) => Slurs.Replace(text, m => new string('*', m.Length));
}
