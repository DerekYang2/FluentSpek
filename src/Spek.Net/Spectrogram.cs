namespace Spek.Net;

// The analysed dB values and their colours. Column x of the image is column x of the analysis, and row 0 is the
// highest frequency. Analysis threads write columns while the UI thread reads pixels, so access goes through a lock.
public sealed class Spectrogram
{
    private readonly object gate = new();
    private readonly float[] values;
    private readonly bool[] hasColumn;
    private readonly byte[] pixels;
    private Palette palette;
    private int lowerDb;
    private int upperDb;
    private int dirtyMin = int.MaxValue;
    private int dirtyMax = -1;
    private int columnsDone;

    public Spectrogram(int columns, int bins, Palette palette, int lowerDb, int upperDb)
    {
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (bins <= 0) throw new ArgumentOutOfRangeException(nameof(bins));

        Columns = columns;
        Bins = bins;
        values = new float[columns * bins];
        hasColumn = new bool[columns];
        pixels = new byte[columns * bins * 4];
        for (int i = 3; i < pixels.Length; i += 4)
        {
            pixels[i] = 255;
        }
        SetRangeAndPalette(palette, lowerDb, upperDb);
    }

    public int Columns { get; }

    public int Bins { get; }

    // Pixel data in BGRA order, Columns wide and Bins high.
    public byte[] Pixels => pixels;

    public int ColumnsDone => Volatile.Read(ref columnsDone);

    public Palette Palette
    {
        get { lock (gate) return palette; }
    }

    public int LowerDb
    {
        get { lock (gate) return lowerDb; }
    }

    public int UpperDb
    {
        get { lock (gate) return upperDb; }
    }

    public void SetColumn(int x, ReadOnlySpan<float> column)
    {
        if ((uint)x >= (uint)Columns) throw new ArgumentOutOfRangeException(nameof(x));
        if (column.Length < Bins) throw new ArgumentException("Column is shorter than the number of bins.", nameof(column));

        lock (gate)
        {
            column[..Bins].CopyTo(values.AsSpan(x * Bins, Bins));
            if (!hasColumn[x])
            {
                hasColumn[x] = true;
                columnsDone++;
            }
            ColourColumn(x);
            MarkDirty(x, x);
        }
    }

    public void SetRangeAndPalette(Palette palette, int lowerDb, int upperDb)
    {
        if (lowerDb >= upperDb) throw new ArgumentException("The lower limit must be below the upper limit.");

        lock (gate)
        {
            if (this.palette == palette && this.lowerDb == lowerDb && this.upperDb == upperDb) return;
            this.palette = palette;
            this.lowerDb = lowerDb;
            this.upperDb = upperDb;
            for (int x = 0; x < Columns; x++)
            {
                if (hasColumn[x]) ColourColumn(x);
            }
            MarkDirty(0, Columns - 1);
        }
    }

    public float GetValue(int x, int bin)
    {
        lock (gate) return values[x * Bins + bin];
    }

    public bool HasColumn(int x)
    {
        lock (gate) return hasColumn[x];
    }

    // Copies the columns changed since the last call into a buffer Width x Bins pixels in size, and returns their
    // range. Returns false when nothing changed.
    public bool TakeDirty(out int left, out int width, out byte[] region)
    {
        lock (gate)
        {
            if (dirtyMax < 0)
            {
                left = width = 0;
                region = [];
                return false;
            }

            left = dirtyMin;
            width = dirtyMax - dirtyMin + 1;
            region = new byte[width * Bins * 4];
            int rowBytes = width * 4;
            for (int y = 0; y < Bins; y++)
            {
                Buffer.BlockCopy(pixels, (y * Columns + left) * 4, region, y * rowBytes, rowBytes);
            }
            dirtyMin = int.MaxValue;
            dirtyMax = -1;
            return true;
        }
    }

    // Marks every column changed, for example after the GPU copy of the pixels is lost.
    public void MarkAllDirty()
    {
        lock (gate) MarkDirty(0, Columns - 1);
    }

    public byte[] CopyPixels()
    {
        lock (gate) return (byte[])pixels.Clone();
    }

    private void ColourColumn(int x)
    {
        double range = upperDb - lowerDb;
        int offset = x * Bins;
        for (int bin = 0; bin < Bins; bin++)
        {
            double value = Math.Min(upperDb, Math.Max(lowerDb, values[offset + bin]));
            uint colour = Palettes.Map(palette, (value - lowerDb) / range);
            int p = ((Bins - 1 - bin) * Columns + x) * 4;
            pixels[p] = (byte)colour;
            pixels[p + 1] = (byte)(colour >> 8);
            pixels[p + 2] = (byte)(colour >> 16);
        }
    }

    private void MarkDirty(int from, int to)
    {
        dirtyMin = Math.Min(dirtyMin, from);
        dirtyMax = Math.Max(dirtyMax, to);
    }
}
