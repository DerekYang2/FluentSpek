namespace Spek.Net;

public delegate void ColumnHandler(int column, ReadOnlySpan<float> values);

// Turns a stream of mono samples into spectrogram columns, the way Spek's pipeline does. Column i covers frames
// [i * total / columns, (i + 1) * total / columns). Inside a column an FFT runs every n frames, without overlap,
// over the last n frames. A column shorter than n still gets one FFT over the last n frames. The column's value
// for each bin is the average of its FFTs, in dB.
public sealed class SpectrogramPipeline
{
    private readonly int columns;
    private readonly long totalFrames;
    private readonly ColumnHandler onColumn;
    private readonly RealFft fft;
    private readonly float[] window;
    private readonly float[] ring;
    private readonly float[] input;
    private readonly float[] power;
    private readonly float[] sum;
    private readonly int mask;

    private int ringPos;
    private int column;
    private long columnEnd;
    private long frames;
    private long framesInColumn;
    private int ffts;

    public SpectrogramPipeline(int fftBits, WindowFunction windowFunction, int columns, long totalFrames, ColumnHandler onColumn)
    {
        if (fftBits < SpekDefaults.MinFftBits || fftBits > SpekDefaults.MaxFftBits)
            throw new ArgumentOutOfRangeException(nameof(fftBits));
        if (columns <= 0) throw new ArgumentOutOfRangeException(nameof(columns));
        if (totalFrames < 0) throw new ArgumentOutOfRangeException(nameof(totalFrames));

        this.columns = columns;
        this.totalFrames = totalFrames;
        this.onColumn = onColumn;
        fft = new RealFft(fftBits);
        window = WindowFunctions.Create(windowFunction, fft.Size);
        ring = new float[fft.Size];
        input = new float[fft.Size];
        power = new float[fft.Bins];
        sum = new float[fft.Bins];
        mask = fft.Size - 1;
        columnEnd = Boundary(1);
    }

    public int Bins => fft.Bins;

    public int ColumnsDone => column;

    public bool IsDone => column >= columns;

    public void Push(ReadOnlySpan<float> samples)
    {
        EndEmptyColumns();
        foreach (float sample in samples)
        {
            if (column >= columns) return;

            ring[ringPos] = sample;
            ringPos = (ringPos + 1) & mask;
            frames++;
            framesInColumn++;

            if (framesInColumn % fft.Size == 0) RunFft();
            if (frames == columnEnd)
            {
                EndColumn();
                EndEmptyColumns();
            }
        }
    }

    // Call at the end of the stream. The duration that sets the column boundaries is an estimate, so the audio can
    // end a few frames early. The column in progress is then finished with the frames it has.
    public void Complete()
    {
        EndEmptyColumns();
        if (column < columns && framesInColumn > 0) EndColumn();
    }

    private long Boundary(int index) => index >= columns ? totalFrames : (long)((Int128)index * totalFrames / columns);

    // With fewer frames than columns, several boundaries fall on the same frame.
    private void EndEmptyColumns()
    {
        while (column < columns && frames == columnEnd) EndColumn();
    }

    private void RunFft()
    {
        for (int i = 0; i < input.Length; i++)
        {
            input[i] = ring[(ringPos + i) & mask] * window[i];
        }
        fft.PowerDb(input, power);
        for (int i = 0; i < sum.Length; i++)
        {
            sum[i] += power[i];
        }
        ffts++;
    }

    private void EndColumn()
    {
        if (ffts == 0) RunFft();
        for (int i = 0; i < sum.Length; i++)
        {
            sum[i] /= ffts;
        }
        onColumn(column, sum);

        Array.Clear(sum);
        ffts = 0;
        framesInColumn = 0;
        column++;
        columnEnd = Boundary(column + 1);
    }
}
