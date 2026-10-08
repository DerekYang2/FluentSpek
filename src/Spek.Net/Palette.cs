namespace Spek.Net;

public enum Palette
{
    Spectrum,
    Sox,
    Mono,
}

public static class Palettes
{
    public const Palette Default = Palette.Spectrum;

    public static string DisplayName(Palette palette) => palette switch
    {
        Palette.Spectrum => "Spectrum",
        Palette.Sox => "SoX",
        Palette.Mono => "Mono",
        _ => palette.ToString(),
    };

    // Maps a level from 0 to 1 to a colour packed as 0xRRGGBB.
    public static uint Map(Palette palette, double level) => palette switch
    {
        Palette.Spectrum => Spectrum(level),
        Palette.Sox => Sox(level),
        Palette.Mono => Mono(level),
        _ => throw new ArgumentOutOfRangeException(nameof(palette)),
    };

    // Dan Bruton's wavelength-to-colour algorithm as modified by Spek:
    // http://www.physics.sfasu.edu/astro/color/spectra.html
    private static uint Spectrum(double level)
    {
        level *= 0.6625;
        double r = 0, g = 0, b = 0;
        if (level >= 0 && level < 0.15)
        {
            r = (0.15 - level) / (0.15 + 0.075);
            b = 1;
        }
        else if (level >= 0.15 && level < 0.275)
        {
            g = (level - 0.15) / (0.275 - 0.15);
            b = 1;
        }
        else if (level >= 0.275 && level < 0.325)
        {
            g = 1;
            b = (0.325 - level) / (0.325 - 0.275);
        }
        else if (level >= 0.325 && level < 0.5)
        {
            r = (level - 0.325) / (0.5 - 0.325);
            g = 1;
        }
        // Spek stops below 0.6625, which turns values at the upper dB limit black. This includes the top of the range.
        else if (level >= 0.5 && level <= 0.6625)
        {
            r = 1;
            g = (0.6625 - level) / (0.6625 - 0.5);
        }

        double cf = level >= 0 && level < 0.1 ? level / 0.1 : 1;
        cf *= 255;
        return Pack(r * cf, g * cf, b * cf);
    }

    // SoX's default palette, by Rob Sykes.
    private static uint Sox(double level)
    {
        double r = 0;
        if (level >= 0.13 && level < 0.73) r = Math.Sin((level - 0.13) / 0.60 * Math.PI / 2);
        else if (level >= 0.73) r = 1;

        double g = 0;
        if (level >= 0.6 && level < 0.91) g = Math.Sin((level - 0.6) / 0.31 * Math.PI / 2);
        else if (level >= 0.91) g = 1;

        double b = 0;
        if (level < 0.60) b = 0.5 * Math.Sin(level / 0.6 * Math.PI);
        else if (level >= 0.78) b = (level - 0.78) / 0.22;

        return Pack(r * 255, g * 255, b * 255);
    }

    private static uint Mono(double level)
    {
        double v = level * 255;
        return Pack(v, v, v);
    }

    private static uint Pack(double r, double g, double b)
    {
        uint rr = (uint)(r + 0.5), gg = (uint)(g + 0.5), bb = (uint)(b + 0.5);
        return (rr << 16) | (gg << 8) | bb;
    }
}
