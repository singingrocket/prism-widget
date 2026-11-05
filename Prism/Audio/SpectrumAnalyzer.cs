namespace Prism.Audio;

/// <summary>
/// Turns recent audio samples into smoothed 0..1 levels for log-spaced frequency bands.
/// Call <see cref="Update"/> once per animation frame.
/// </summary>
internal sealed class SpectrumAnalyzer
{
    public const int FftSize = 2048;
    private const double MinHz = 40;
    private const double MaxHz = 16000;
    private const double FloorDb = -66;
    private const double CeilingDb = -12;
    private const double TiltDbPerOctave = 3;
    private const double Attack = 0.6;
    private const double Decay = 0.86;

    private readonly float[] _samples = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _re = new float[FftSize];
    private readonly float[] _im = new float[FftSize];
    private readonly double[] _targets;
    private readonly double[] _levels;

    public SpectrumAnalyzer(int bandCount)
    {
        _targets = new double[bandCount];
        _levels = new double[bandCount];
        for (int i = 0; i < FftSize; i++)
        {
            _window[i] = (float)(0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1))));
        }
    }

    public IReadOnlyList<double> Levels => _levels;

    /// <summary>Extra sensitivity in decibels; microphones deliver much lower levels than playback.</summary>
    public double GainDb { get; set; }

    public void Update(AudioCapture capture)
    {
        if (capture.TryGetLatest(_samples))
        {
            ComputeTargets(capture.SampleRate);
        }
        else
        {
            Array.Clear(_targets);
        }

        for (int i = 0; i < _levels.Length; i++)
        {
            double target = _targets[i];
            double level = _levels[i];
            _levels[i] = target > level ? level + (target - level) * Attack : level * Decay;
        }
    }

    private void ComputeTargets(int sampleRate)
    {
        for (int i = 0; i < FftSize; i++)
        {
            _re[i] = _samples[i] * _window[i];
            _im[i] = 0;
        }
        Fft(_re, _im);

        double binHz = (double)sampleRate / FftSize;
        int bands = _targets.Length;
        for (int band = 0; band < bands; band++)
        {
            double lowHz = MinHz * Math.Pow(MaxHz / MinHz, (double)band / bands);
            double highHz = MinHz * Math.Pow(MaxHz / MinHz, (double)(band + 1) / bands);
            int lowBin = Math.Clamp((int)(lowHz / binHz), 1, FftSize / 2 - 1);
            int highBin = Math.Clamp((int)Math.Ceiling(highHz / binHz) - 1, lowBin, FftSize / 2 - 1);

            double peak = 0;
            for (int bin = lowBin; bin <= highBin; bin++)
            {
                peak = Math.Max(peak, Math.Sqrt(_re[bin] * _re[bin] + _im[bin] * _im[bin]));
            }

            // A full-scale sine under a Hann window peaks at FftSize / 4.
            double db = 20 * Math.Log10(peak / (FftSize / 4.0) + 1e-9) + GainDb;
            db += TiltDbPerOctave * Math.Log2(Math.Sqrt(lowHz * highHz) / 1000);
            _targets[band] = Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0, 1);
        }
    }

    /// <summary>In-place iterative radix-2 FFT.</summary>
    private static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int length = 2; length <= n; length <<= 1)
        {
            double angle = -2 * Math.PI / length;
            float stepRe = (float)Math.Cos(angle);
            float stepIm = (float)Math.Sin(angle);
            int half = length / 2;
            for (int start = 0; start < n; start += length)
            {
                float wRe = 1, wIm = 0;
                for (int k = 0; k < half; k++)
                {
                    int a = start + k, b = a + half;
                    float tRe = re[b] * wRe - im[b] * wIm;
                    float tIm = re[b] * wIm + im[b] * wRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    float nextRe = wRe * stepRe - wIm * stepIm;
                    wIm = wRe * stepIm + wIm * stepRe;
                    wRe = nextRe;
                }
            }
        }
    }
}
