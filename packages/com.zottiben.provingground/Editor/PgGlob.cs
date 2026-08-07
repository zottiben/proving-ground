using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ProvingGround.EditorTools
{
    /// <summary>
    /// Glob matching for asset paths, so content rules can be written the way people
    /// actually write them rather than as regular expressions.
    /// </summary>
    public static class PgGlob
    {
        static readonly Dictionary<string, Regex> Cache = new Dictionary<string, Regex>();

        /// <summary>
        /// Matches <paramref name="path"/> against a glob. <c>*</c> matches within a path
        /// segment, <c>**</c> matches across segments, <c>?</c> matches one character.
        /// </summary>
        public static bool Matches(string path, string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            if (string.IsNullOrEmpty(path)) return false;

            if (!Cache.TryGetValue(pattern, out var regex))
            {
                regex = new Regex(ToRegex(pattern), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                Cache[pattern] = regex;
            }

            return regex.IsMatch(path.Replace('\\', '/'));
        }

        public static bool MatchesAny(string path, IEnumerable<string> patterns)
        {
            if (patterns == null) return false;
            foreach (var pattern in patterns)
                if (Matches(path, pattern))
                    return true;
            return false;
        }

        static string ToRegex(string pattern)
        {
            var builder = new StringBuilder("^");

            for (var i = 0; i < pattern.Length; i++)
            {
                var c = pattern[i];
                switch (c)
                {
                    case '*':
                        if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                        {
                            // '**/' should also match zero directories, so that
                            // Assets/**/UI/*.png matches Assets/UI/a.png.
                            if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                            {
                                builder.Append("(?:.*/)?");
                                i += 2;
                            }
                            else
                            {
                                builder.Append(".*");
                                i++;
                            }
                        }
                        else builder.Append("[^/]*");

                        break;

                    case '?':
                        builder.Append("[^/]");
                        break;

                    default:
                        builder.Append(Regex.Escape(c.ToString()));
                        break;
                }
            }

            builder.Append('$');
            return builder.ToString();
        }
    }
}
