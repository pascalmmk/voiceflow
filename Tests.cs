namespace VoiceFlow;

public static class Tests
{
    public static void Run(string logPath)
    {
        var results = new List<string>();
        void Check(bool condition, string title) { if (!condition) throw new Exception(title); results.Add("PASS " + title); }
        var off = new Settings(false, -42, 180, 35, false, 40, false, -20, 3, false, -18);
        Audio Tone(double seconds, float level, int channels = 1)
        {
            int rate = 16000; var data = new float[(int)(seconds * rate) * channels];
            for (int i = 0; i < data.Length / channels; i++) for (int c = 0; c < channels; c++)
                data[i * channels + c] = level * (float)Math.Sin(i * 2 * Math.PI * 220 / rate) * (c == 0 ? 1 : -1);
            return new Audio(data, rate, channels);
        }
        try
        {
            var tone = Tone(2, .2f, 2);
            var bypass = Dsp.Process(tone, off, [], default);
            Check(tone.Samples.SequenceEqual(bypass.Audio.Samples), "Bypass preserves every sample and channel");
            var radio = Dsp.Process(tone, off with { RetroRadio = true }, [], default).Audio;
            Check(radio.Frames == tone.Frames && radio.Rate == tone.Rate && radio.Channels == tone.Channels && !radio.Samples.SequenceEqual(tone.Samples), "Radio changes timbre while preserving duration and format");
            Check(radio.Samples.All(x => float.IsFinite(x) && Math.Abs(x) <= .86), "Radio output is finite and bounded without normalization");
            Check(Enumerable.Range(0, radio.Frames).All(i => radio.Samples[i * 2] == -radio.Samples[i * 2 + 1]), "Radio preserves opposite-phase stereo without cancellation");
            var silentRadio = Dsp.Process(new Audio(new float[16000], 16000, 1), off with { RetroRadio = true }, [], default);
            Check(silentRadio.Audio.Samples.All(x => x == 0), "Radio does not introduce noise into silence");
            var quietRadio = Dsp.Process(Tone(2, .08f), off with { RetroRadio = true }, [], default);
            var loudRadio = Dsp.Process(Tone(2, .8f), off with { RetroRadio = true }, [], default);
            Check(Math.Abs(loudRadio.RmsDb - quietRadio.RmsDb) < 5, "Radio strongly compresses a 20 dB input level difference");
            var normalized = Dsp.Process(tone, off with { Normalize = true }, [], default);
            Check(Math.Abs(normalized.RmsDb + 18) < .01, "RMS normalization reaches target");
            var compressed = Dsp.Process(Tone(2, .8f), off with { Compression = true }, [], default);
            Check(compressed.RmsDb < -12, "Compressor reduces sustained loud speech-level audio");
            var stereo = Dsp.Process(tone, off with { Compression = true }, [], default).Audio;
            Check(Enumerable.Range(0, stereo.Frames).All(i => stereo.Samples[i * 2] == -stereo.Samples[i * 2 + 1]), "Stereo-linked compression preserves channel balance");
            var quiet = Tone(2, .005f); quiet.Samples[900] = 1;
            var capped = Dsp.Process(quiet, off with { Normalize = true }, [], default);
            Check(capped.Limited && capped.PeakDb <= -0.999, "Peak protection caps normalization gain");
            var pause = Tone(3, .2f, 2); Array.Clear(pause.Samples, 16000 * 2, 16000 * 2);
            var cuts = Dsp.Analyze(pause, off with { Silence = true }, default);
            Check(cuts.Count == 1 && cuts[0].Start >= 16000 && cuts[0].End <= 32000, "Silence detection with opposite-phase stereo");
            var trimmed = Dsp.Process(pause, off, cuts, default).Audio;
            Check(trimmed.Seconds > 2 && trimmed.Seconds < 2.1, "Pause removal preserves edge padding");
            Check(Dsp.Process(pause, off, [], default).Audio.Frames == pause.Frames, "Unchecking cuts restores the original duration");
            var overlap = Dsp.Process(tone, off, [new Cut(1000, 2000, "test"), new Cut(1500, 3000, "test")], default);
            Check(overlap.Audio.Frames == tone.Frames - 2000 - 80, "Overlapping cuts merge and crossfade exactly once");
            var noise = new Audio(new float[16000], 16000, 1); var random = new Random(7);
            for (int i = 4000; i < 8000; i++) noise.Samples[i] = (float)((random.NextDouble() * 2 - 1) * .04);
            Check(Dsp.Analyze(noise, off with { Breaths = true, BreathStrength = 80 }, default).Any(c => c.Kind == "Possible breath"), "Breath heuristic finds a synthetic isolated noise burst");
            Check(!Dsp.Analyze(tone, off with { Breaths = true }, default).Any(), "Breath heuristic preserves a voiced test tone");
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel(); bool canceled = false;
                try { Dsp.Analyze(tone, off, cts.Token); } catch (OperationCanceledException) { canceled = true; }
                Check(canceled, "Analysis honors cancellation");
            }
            bool emptyRejected = false;
            try { Dsp.Process(tone, off, [new Cut(0, tone.Frames, "all")], default); } catch (InvalidOperationException) { emptyRejected = true; }
            Check(emptyRejected, "Rejects settings that remove the entire recording");
            string scratch = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(logPath))!, "roundtrip.wav");
            try
            {
                Wave.Save(scratch, tone); var reread = Wave.Read(scratch);
                Check(reread.Rate == tone.Rate && reread.Channels == 2 && reread.Frames == tone.Frames, "WAV roundtrip preserves metadata and length");
                Check(tone.Samples.Zip(reread.Samples).All(p => Math.Abs(p.First - p.Second) < 1.0 / 32768), "16-bit export has expected quantization accuracy");
                File.WriteAllBytes(scratch, [1, 2, 3]); bool malformed = false;
                try { Wave.Read(scratch); } catch (InvalidDataException) { malformed = true; }
                Check(malformed, "Malformed WAV is rejected");
            }
            finally { File.Delete(scratch); }
            results.Add($"All {results.Count} checks passed.");
            File.WriteAllLines(logPath, results);
        }
        catch (Exception ex) { results.Add("FAIL " + ex); File.WriteAllLines(logPath, results); Environment.ExitCode = 1; }
    }
}
