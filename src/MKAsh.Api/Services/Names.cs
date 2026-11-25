using System.Text;
using System.Text.RegularExpressions;

namespace MKAsh.Api.Services;

/// <summary>
/// Person-name matching, identical to the dashboard's (nameMatch): Arabic letter forms and diacritics
/// are normalised, titles such as "د." or "الدكتورة" are dropped, and two names match when they are equal
/// or share the first and last word. Used to link project managers and sector leads to projects.
/// </summary>
public static class Names
{
    private static readonly Regex Title = new(@"^(الدكتوره|الدكتور|د\\|د\.|د |ا\.|ا )\s*", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    public static string NormText(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c0 in s.ToLowerInvariant())
        {
            if ((c0 >= 'ً' && c0 <= 'ْ') || c0 == 'ـ' || (c0 >= '​' && c0 <= '‏') || (c0 >= '‪' && c0 <= '‮')) continue;
            sb.Append(c0 switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ة' => 'ه',
                'ى' => 'ي',
                'ؤ' => 'و',
                'ئ' => 'ي',
                _ => c0,
            });
        }
        return sb.ToString();
    }

    public static string Norm(string? s)
    {
        var x = NormText((s ?? "").Trim());
        x = Title.Replace(x, "");
        x = x.Replace("عبد ال", "عبدال");
        return Spaces.Replace(x, " ").Trim();
    }

    public static bool Match(string? a, string? b)
    {
        var A = Norm(a).Split(' ');
        var B = Norm(b).Split(' ');
        if (A[0].Length == 0 || B[0].Length == 0) return false;
        if (string.Join(' ', A) == string.Join(' ', B)) return true;
        return A[0] == B[0] && A[^1] == B[^1];
    }
}
