using System;

namespace NoireLib.Localizer;

// CLDR cardinal plural rules for whole numbers.
internal static class PluralRules
{
    private static readonly PluralCategory[] OtherOnly = [PluralCategory.Other];
    private static readonly PluralCategory[] OneOther = [PluralCategory.One, PluralCategory.Other];
    private static readonly PluralCategory[] OneFewMany = [PluralCategory.One, PluralCategory.Few, PluralCategory.Many];
    private static readonly PluralCategory[] OneFewOther = [PluralCategory.One, PluralCategory.Few, PluralCategory.Other];
    private static readonly PluralCategory[] OneTwoOther = [PluralCategory.One, PluralCategory.Two, PluralCategory.Other];
    private static readonly PluralCategory[] OneTwoFewOther = [PluralCategory.One, PluralCategory.Two, PluralCategory.Few, PluralCategory.Other];
    private static readonly PluralCategory[] Arabic = [PluralCategory.Zero, PluralCategory.One, PluralCategory.Two, PluralCategory.Few, PluralCategory.Many, PluralCategory.Other];

    private enum Family
    {
        OneOther,
        OneOtherMillions,
        ZeroOneOtherMillions,
        OtherOnly,
        EastSlavic,
        Polish,
        Czech,
        SerboCroatian,
        Lithuanian,
        Romanian,
        Slovenian,
        Hebrew,
        Arabic,
    }

    internal static PluralCategory For(string language, int count)
    {
        var n = Math.Abs((long)count);
        var mod10 = n % 10;
        var mod100 = n % 100;

        var family = FamilyOf(language);

        switch (family)
        {
            case Family.OtherOnly:
                return PluralCategory.Other;

            case Family.OneOtherMillions:
                return n == 1 ? PluralCategory.One : IsMillions(n) ? PluralCategory.Many : PluralCategory.Other;

            case Family.ZeroOneOtherMillions:
                return n <= 1 ? PluralCategory.One : IsMillions(n) ? PluralCategory.Many : PluralCategory.Other;

            case Family.EastSlavic:
            case Family.Polish:
                if (family == Family.EastSlavic ? mod10 == 1 && mod100 != 11 : n == 1)
                    return PluralCategory.One;

                return mod10 is >= 2 and <= 4 && mod100 is not (>= 12 and <= 14) ? PluralCategory.Few : PluralCategory.Many;

            case Family.Czech:
                return n == 1 ? PluralCategory.One : n is >= 2 and <= 4 ? PluralCategory.Few : PluralCategory.Other;

            case Family.SerboCroatian:
                if (mod10 == 1 && mod100 != 11)
                    return PluralCategory.One;

                return mod10 is >= 2 and <= 4 && mod100 is not (>= 12 and <= 14) ? PluralCategory.Few : PluralCategory.Other;

            case Family.Lithuanian:
                if (mod100 is >= 11 and <= 19)
                    return PluralCategory.Other;

                return mod10 == 1 ? PluralCategory.One : mod10 >= 2 ? PluralCategory.Few : PluralCategory.Other;

            case Family.Romanian:
                return n == 1 ? PluralCategory.One : n == 0 || mod100 is >= 1 and <= 19 ? PluralCategory.Few : PluralCategory.Other;

            case Family.Slovenian:
                return mod100 switch
                {
                    1 => PluralCategory.One,
                    2 => PluralCategory.Two,
                    3 or 4 => PluralCategory.Few,
                    _ => PluralCategory.Other,
                };

            case Family.Hebrew:
                return n switch
                {
                    1 => PluralCategory.One,
                    2 => PluralCategory.Two,
                    _ => PluralCategory.Other,
                };

            case Family.Arabic:
                if (n <= 2)
                    return (PluralCategory)(int)n;

                return mod100 switch
                {
                    >= 3 and <= 10 => PluralCategory.Few,
                    >= 11 => PluralCategory.Many,
                    _ => PluralCategory.Other,
                };

            default:
                return n == 1 ? PluralCategory.One : PluralCategory.Other;
        }
    }

    // The categories a translation must provide. Many for exact millions (French, Spanish, Italian, Portuguese,
    // Catalan) is left out: it falls back to Other, which reads correctly in nearly every text.
    internal static PluralCategory[] CategoriesOf(string language) => FamilyOf(language) switch
    {
        Family.OtherOnly => OtherOnly,
        Family.EastSlavic or Family.Polish => OneFewMany,
        Family.Czech or Family.SerboCroatian or Family.Lithuanian or Family.Romanian => OneFewOther,
        Family.Slovenian => OneTwoFewOther,
        Family.Hebrew => OneTwoOther,
        Family.Arabic => Arabic,
        _ => OneOther,
    };

    internal static string Suffix(PluralCategory category) => category switch
    {
        PluralCategory.Zero => "zero",
        PluralCategory.One => "one",
        PluralCategory.Two => "two",
        PluralCategory.Few => "few",
        PluralCategory.Many => "many",
        _ => "other",
    };

    private static bool IsMillions(long n) => n != 0 && n % 1000000 == 0;

    private static Family FamilyOf(string language)
    {
        if (language.StartsWith("pt-PT", StringComparison.OrdinalIgnoreCase))
            return Family.OneOtherMillions;

        var dash = language.IndexOf('-');
        var code = dash < 0 ? language : language[..dash];

        return code.ToLowerInvariant() switch
        {
            "ja" or "ko" or "zh" or "yue" or "th" or "vi" or "id" or "ms" or "lo" or "my" or "km" => Family.OtherOnly,
            "fr" or "pt" => Family.ZeroOneOtherMillions,
            "es" or "it" or "ca" => Family.OneOtherMillions,
            "ru" or "uk" or "be" => Family.EastSlavic,
            "pl" => Family.Polish,
            "cs" or "sk" => Family.Czech,
            "hr" or "sr" or "bs" => Family.SerboCroatian,
            "lt" => Family.Lithuanian,
            "ro" or "mo" => Family.Romanian,
            "sl" => Family.Slovenian,
            "he" or "iw" => Family.Hebrew,
            "ar" => Family.Arabic,
            _ => Family.OneOther,
        };
    }
}
