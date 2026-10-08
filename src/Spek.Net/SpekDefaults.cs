namespace Spek.Net;

public static class SpekDefaults
{
    public const int FftBits = 11;
    public const int MinFftBits = 8;
    public const int MaxFftBits = 14;

    public const int UpperDb = 0;
    public const int LowerDb = -120;
    public const int MaxDb = 0;
    public const int MinDb = -140;

    public static int BinsFor(int fftBits) => (1 << (fftBits - 1)) + 1;
}
