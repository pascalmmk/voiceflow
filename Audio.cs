using System.Text;

namespace VoiceFlow;

public sealed record Audio(float[] Samples, int Rate, int Channels)
{
    public int Frames => Samples.Length / Channels;
    public double Seconds => Frames / (double)Rate;
}
public sealed record Cut(int Start, int End, string Kind);
public sealed record Settings(bool Silence, double SilenceDb, int MinimumPauseMs, int PaddingMs,
    bool Breaths, int BreathStrength, bool Compression, double ThresholdDb, double Ratio,
    bool Normalize, double TargetDb, bool RetroRadio = false);
public sealed record Result(Audio Audio, double PeakDb, double RmsDb, double GainDb, bool Limited);

public static class Wave
{
    public static Audio Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var r = new BinaryReader(stream);
        string Id() => Encoding.ASCII.GetString(r.ReadBytes(4));
        if (stream.Length < 12 || Id() != "RIFF") throw new InvalidDataException("Choose an uncompressed WAV file.");
        r.ReadUInt32();
        if (Id() != "WAVE") throw new InvalidDataException("This is not a WAV file.");
        int format = 0, channels = 0, rate = 0, bits = 0, align = 0;
        long offset = 0; int length = 0;
        while (stream.Position + 8 <= stream.Length)
        {
            string id = Id(); uint size = r.ReadUInt32(); long start = stream.Position;
            long next = start + size + (size & 1);
            if (start + size > stream.Length) throw new InvalidDataException("The WAV file is truncated.");
            if (id == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("Invalid WAV format header.");
                format = r.ReadUInt16(); channels = r.ReadUInt16(); rate = r.ReadInt32();
                r.ReadUInt32(); align = r.ReadUInt16(); bits = r.ReadUInt16();
                if (format == 65534 && size >= 40)
                {
                    r.ReadUInt16(); int validBits = r.ReadUInt16(); r.ReadUInt32();
                    byte[] guid = r.ReadBytes(16);
                    var sub = new Guid(guid);
                    format = sub == new Guid("00000001-0000-0010-8000-00aa00389b71") ? 1 :
                        sub == new Guid("00000003-0000-0010-8000-00aa00389b71") ? 3 : 0;
                    if (validBits != 0 && validBits != bits) throw new InvalidDataException("WAV with packed valid bits is not supported. Export standard PCM WAV first.");
                }
            }
            else if (id == "data" && offset == 0)
            {
                if (size > 512 * 1024 * 1024) throw new InvalidDataException("Use a WAV file smaller than 512 MB.");
                offset = start; length = (int)size;
            }
            stream.Position = next;
        }
        if (channels is < 1 or > 2 || rate is < 8000 or > 192000 || offset == 0 || length == 0 ||
            !((format == 1 && bits is 8 or 16 or 24 or 32) || (format == 3 && bits == 32)) ||
            align != channels * (bits / 8) || length % align != 0)
            throw new InvalidDataException("Supported: mono/stereo PCM WAV (8/16/24/32-bit) or 32-bit float WAV, 8–192 kHz.");
        stream.Position = offset;
        float[] samples = new float[length / (bits / 8)];
        for (int i = 0; i < samples.Length; i++)
        {
            float value;
            if (format == 3) value = r.ReadSingle();
            else if (bits == 8) value = (r.ReadByte() - 128) / 128f;
            else if (bits == 16) value = r.ReadInt16() / 32768f;
            else if (bits == 24)
            {
                int n = r.ReadByte() | (r.ReadByte() << 8) | (r.ReadByte() << 16);
                if ((n & 0x800000) != 0) n |= unchecked((int)0xff000000);
                value = n / 8388608f;
            }
            else value = r.ReadInt32() / 2147483648f;
            samples[i] = float.IsFinite(value) ? Math.Clamp(value, -1f, 1f) : 0;
        }
        return new Audio(samples, rate, channels);
    }

    public static void Write(Stream stream, Audio audio)
    {
        using var w = new BinaryWriter(stream, Encoding.ASCII, true);
        int size = checked(audio.Samples.Length * 2);
        w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(size + 36); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        w.Write(16); w.Write((short)1); w.Write((short)audio.Channels); w.Write(audio.Rate);
        w.Write(audio.Rate * audio.Channels * 2); w.Write((short)(audio.Channels * 2)); w.Write((short)16);
        w.Write(Encoding.ASCII.GetBytes("data")); w.Write(size);
        foreach (float s in audio.Samples) w.Write((short)Math.Clamp(Math.Round(s * 32768.0), -32768, 32767));
    }
    public static void Save(string path, Audio audio)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { using (var s = File.Create(temp)) Write(s, audio); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

public static class Dsp
{
    public static double Db(double value) => 20 * Math.Log10(Math.Max(value, 1e-10));
    public static double Amp(double db) => Math.Pow(10, db / 20);

    public static List<Cut> Analyze(Audio a, Settings s, CancellationToken token)
    {
        int hop = Math.Max(1, a.Rate / 100), count = (a.Frames + hop - 1) / hop;
        var rms = new double[count]; var breath = new bool[count];
        double gate = Amp(s.SilenceDb);
        for (int f = 0; f < count; f++)
        {
            token.ThrowIfCancellationRequested();
            int start = f * hop, end = Math.Min(a.Frames, start + hop), dominant = 0;
            double energy = 0;
            for (int c = 0; c < a.Channels; c++)
            {
                double sum = 0;
                for (int i = start; i < end; i++) { double x = a.Samples[i * a.Channels + c]; sum += x * x; }
                if (sum > energy) { energy = sum; dominant = c; }
            }
            rms[f] = Math.Sqrt(energy / (end - start));
            // High-frequency, low-level noise is only a breath candidate. It is not a speech classifier.
            double high = 0, low = 0, alpha = 1 - Math.Exp(-2 * Math.PI * 1800 / a.Rate);
            for (int i = start; i < end; i++)
            {
                double x = a.Samples[i * a.Channels + dominant]; low += alpha * (x - low);
                high += (x - low) * (x - low);
            }
            double ceiling = Amp(-35 + s.BreathStrength * 0.15);
            double whiteNoiseRatio = 2 * (1 - alpha) * (1 - alpha) / (2 - alpha);
            breath[f] = rms[f] > gate && rms[f] < ceiling && high / Math.Max(energy, 1e-12) / whiteNoiseRatio > 1.10 - s.BreathStrength * 0.005;
        }
        var cuts = new List<Cut>();
        if (s.Silence)
        {
            int f = 0;
            while (f < count)
            {
                if (rms[f] >= gate) { f++; continue; }
                int first = f; while (f < count && rms[f] < gate) f++;
                int start = first * hop, end = Math.Min(a.Frames, f * hop);
                if ((end - start) * 1000.0 / a.Rate < s.MinimumPauseMs) continue;
                int pad = s.PaddingMs * a.Rate / 1000;
                int left = start == 0 ? 0 : start + pad;
                int right = end == a.Frames ? end : end - pad;
                if (right > left) cuts.Add(new Cut(left, right, "Silence"));
            }
        }
        if (s.Breaths)
        {
            int f = 0;
            while (f < count)
            {
                if (!breath[f]) { f++; continue; }
                int first = f; while (f < count && breath[f]) f++;
                int duration = (f - first) * 10;
                // Only suggest isolated noise near a pause; protect sustained fricatives inside words.
                bool nearPause = Enumerable.Range(Math.Max(0, first - 15), Math.Min(count, f + 15) - Math.Max(0, first - 15)).Any(i => rms[i] < gate);
                if (duration >= 80 && duration <= 700 && nearPause)
                    cuts.Add(new Cut(first * hop, Math.Min(a.Frames, f * hop), "Possible breath"));
            }
        }
        return cuts.OrderBy(x => x.Start).ToList();
    }

    public static Result Process(Audio a, Settings s, IReadOnlyList<Cut> cuts, CancellationToken token)
    {
        var merged = new List<Cut>();
        foreach (var cut in cuts.OrderBy(c => c.Start))
        {
            if (cut.Start < 0 || cut.End > a.Frames || cut.Start >= cut.End) throw new ArgumentException("Invalid cut range.");
            if (merged.Count > 0 && cut.Start <= merged[^1].End)
                merged[^1] = merged[^1] with { End = Math.Max(merged[^1].End, cut.End) };
            else merged.Add(cut);
        }
        var segments = new List<(int Start, int End)>(); int pos = 0;
        foreach (var c in merged) { if (c.Start > pos) segments.Add((pos, c.Start)); pos = c.End; }
        if (pos < a.Frames) segments.Add((pos, a.Frames));
        if (segments.Count == 0) throw new InvalidOperationException("These settings remove the entire recording. Lower the silence threshold or uncheck cuts.");
        int capacity = segments.Sum(x => x.End - x.Start);
        float[] samples = new float[capacity * a.Channels]; int written = 0;
        foreach (var segment in segments)
        {
            token.ThrowIfCancellationRequested();
            int length = segment.End - segment.Start;
            int overlap = Math.Min(a.Rate * 5 / 1000, Math.Min(written, length / 2));
            for (int j = 0; j < overlap; j++)
            {
                double t = (j + 1.0) / (overlap + 1);
                for (int c = 0; c < a.Channels; c++)
                {
                    int dst = (written - overlap + j) * a.Channels + c;
                    samples[dst] = (float)(samples[dst] * (1 - t) + a.Samples[(segment.Start + j) * a.Channels + c] * t);
                }
            }
            Array.Copy(a.Samples, (segment.Start + overlap) * a.Channels, samples, written * a.Channels, (length - overlap) * a.Channels);
            written += length - overlap;
        }
        Array.Resize(ref samples, written * a.Channels);
        if (merged.Count > 0)
        {
            int fade = Math.Min(a.Rate * 3 / 1000, written / 2);
            for (int i = 0; i < fade; i++) for (int c = 0; c < a.Channels; c++)
            {
                samples[i * a.Channels + c] *= i / (float)fade;
                samples[(written - 1 - i) * a.Channels + c] *= i / (float)fade;
            }
        }
        if (s.Compression)
        {
            double envelope = 0, attack = Math.Exp(-1.0 / (a.Rate * .005)), release = Math.Exp(-1.0 / (a.Rate * .100));
            for (int i = 0; i < written; i++)
            {
                if (i % 8192 == 0) token.ThrowIfCancellationRequested();
                double peak = 0;
                for (int c = 0; c < a.Channels; c++) peak = Math.Max(peak, Math.Abs(samples[i * a.Channels + c]));
                double coeff = peak > envelope ? attack : release;
                envelope = coeff * envelope + (1 - coeff) * peak;
                double over = Math.Max(0, Db(envelope) - s.ThresholdDb);
                double gain = Amp(-over * (1 - 1 / s.Ratio));
                for (int c = 0; c < a.Channels; c++) samples[i * a.Channels + c] *= (float)gain;
            }
        }
        if (s.RetroRadio) ApplyRetroRadio(samples, a.Rate, a.Channels, token);
        double square = 0, max = 0;
        foreach (float x in samples) { square += x * (double)x; max = Math.Max(max, Math.Abs(x)); }
        double rmsValue = Math.Sqrt(square / samples.Length), gainValue = 1;
        bool limited = false;
        if (s.Normalize && rmsValue > 1e-9)
        {
            double requested = Amp(s.TargetDb) / rmsValue;
            gainValue = Math.Min(requested, Amp(-1) / Math.Max(max, 1e-9));
            limited = gainValue < requested - 1e-6;
            for (int i = 0; i < samples.Length; i++)
            {
                if (i % 16384 == 0) token.ThrowIfCancellationRequested();
                samples[i] *= (float)gainValue;
            }
        }
        return new Result(new Audio(samples, a.Rate, a.Channels), Db(max * gainValue), Db(rmsValue * gainValue), Db(gainValue), limited);
    }

    // A stylized old-game radio effect, not an emulation of a specific game codec.
    static void ApplyRetroRadio(float[] samples, int rate, int channels, CancellationToken token)
    {
        var bass = new double[channels];
        var treble = new double[channels];
        var treble2 = new double[channels];
        var held = new float[channels];
        double highPass = 1 - Math.Exp(-2 * Math.PI * 300 / rate);
        double lowPass = 1 - Math.Exp(-2 * Math.PI * 3000 / rate);
        double attack = Math.Exp(-1.0 / (rate * .001));
        double release = Math.Exp(-1.0 / (rate * .060));
        double envelope = 0, phase = 1, step = Math.Min(11025.0, rate) / rate;
        for (int i = 0; i < samples.Length / channels; i++)
        {
            if (i % 8192 == 0) token.ThrowIfCancellationRequested();
            double peak = 0;
            for (int c = 0; c < channels; c++)
            {
                double x = samples[i * channels + c];
                bass[c] += highPass * (x - bass[c]);
                treble[c] += lowPass * (x - bass[c] - treble[c]);
                treble2[c] += lowPass * (treble[c] - treble2[c]);
                peak = Math.Max(peak, Math.Abs(treble2[c]));
            }
            double coefficient = peak > envelope ? attack : release;
            envelope = coefficient * envelope + (1 - coefficient) * peak;
            // Fixed 20:1 compression at -30 dBFS, with 20 dB makeup gain.
            double gain = Amp(20 - Math.Max(0, Db(envelope) + 30) * .95);
            bool capture = phase >= 1;
            if (capture) phase -= 1;
            for (int c = 0; c < channels; c++)
            {
                if (capture)
                {
                    double driven = .85 * Math.Tanh(treble2[c] * gain * 2);
                    held[c] = (float)(Math.Round(driven * 127) / 127);
                }
                samples[i * channels + c] = held[c];
            }
            phase += step;
        }
    }
}
