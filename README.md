# VoiceFlow

A local Windows desktop app for tightening spoken recordings and evening out voice volume.

## Run

Open `App/VoiceFlow.exe`. Keep the files in that folder together. The app uses the .NET 8 Desktop Runtime, which is installed on the computer used to build it. Other Windows computers need that runtime installed.

When cloning this repository, build the app first using the Development commands below. Generated binaries in `App`, `bin`, and `obj` are excluded from Git.

## Workflow

1. Click **Open WAV** and select a recording.
2. Adjust silence removal, breath detection, compression, and normalization.
3. Click **Analyze & process**.
4. Compare **Play original** and **Play result**. Select a cut and click **Hear selected cut** to hear the original audio around it.
5. Uncheck any cuts you want to keep, then click **Apply selected cuts**.
6. Click **Export WAV** to save a new recording. The app prevents overwriting the imported path.

Changing settings clears the detected cuts and requires another analysis. Changing a cut invalidates the processed result until you apply your selection. Every pass starts from the original audio, so effects do not accumulate.

## Controls

- **Silence threshold:** Sections below this level count as quiet. Lower values such as −50 dBFS protect more quiet speech; higher values remove more low-level sound.
- **Minimum silence:** Shorter values tighten more pauses.
- **Keep at each speech edge:** Padding protects word endings and beginnings. Set to zero for tighter edits. Cuts use a short crossfade to soften joins.
- **Breath sensitivity:** Higher values suggest more possible breaths. Detection uses noise characteristics and proximity to pauses. It is experimental and can miss breaths or select quiet consonants, background noise, or other sounds. Audition the cuts; it does not reliably remove every breath.
- **Compressor threshold / ratio:** Reduce louder sections to make the voice more even. The compressor uses linked channels with a 5 ms attack and 100 ms release.
- **Target RMS:** Sets average signal level after processing. This is RMS normalization, not LUFS normalization. Gain is capped to keep sample peaks at −1 dBFS, so the target may not be reached on recordings with large transients. This is sample-peak protection, not a true-peak limiter.

## Supported audio

Input: mono/stereo uncompressed WAV, 8/16/24/32-bit PCM or 32-bit float, 8–192 kHz. Output: 16-bit PCM WAV at the original sample rate and channel count. MP3, AAC, FLAC, and compressed WAV input are not supported in this version. WAV data is limited to 512 MB; processing uses memory proportional to recording length.

Processing and playback are local. There are no online services, accounts, or package dependencies.

## Development

The included C# source targets .NET 8 Windows Forms. With the .NET 8 SDK installed:

```powershell
dotnet build VoiceFlow.csproj -c Release
dotnet publish VoiceFlow.csproj -c Release --no-self-contained -o App
```

The included NuGet configuration disables remote package sources because this project needs none.

`VoiceFlow.exe --self-test <absolute-log-path>` runs the signal-processing checks. `VoiceFlow.exe --ui-test <absolute-log-path>` runs the UI processing/review workflow and saves a rendered preview beside the log. Tests use synthetic signals; breath detection accuracy has not been evaluated on a real speech dataset, and speaker playback has not been verified by listening.

Windows WAV playback uses [System.Media.SoundPlayer](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/controls/soundplayer-class-overview).

## Technologies

| Technology | Role |
|---|---|
| C# | Implements the interface, file handling, and audio algorithms. |
| .NET 8 | Runs the compiled application and supplies framework libraries. |
| Windows Forms | Supplies the desktop window, controls, and drawing surface. |
| System.Media.SoundPlayer | Plays WAV audio from an in-memory stream. |
| Custom WAV reader and writer | Converts WAV bytes to samples and exports processed samples. |
| Custom digital signal processing (DSP) | Detects cuts, joins audio, compresses dynamics, and normalizes volume. |

This version uses conventional signal processing. It has no AI model, speech recognizer, cloud service, or third-party audio package.

## Processing flow

```text
Open WAV
   |
   v
Decode file into sample values
   |
   v
Analyze 10 ms windows for silence and possible breaths
   |
   v
Merge selected cuts and copy the remaining sections
   |
   v
Crossfade joins -> Compress dynamics -> Normalize RMS level
   |
   v
Preview result -> Export 16-bit WAV
```

Detection examines the original audio before compression or normalization changes its level. Every processing pass starts from that original audio. Cut selections and settings determine a fresh result.

## File guide

### Source and documentation

| File | What it contains |
|---|---|
| `Audio.cs` | Audio data records, WAV reading/writing, cut detection, and processing algorithms. |
| `Program.cs` | Startup, the entire Windows Forms interface, playback, export, waveform drawing, and a UI workflow test. |
| `Tests.cs` | A small test runner that checks processing with generated signals. |
| `VoiceFlow.csproj` | XML project settings used by the .NET build tools. |
| `NuGet.Config` | Package-source configuration for dependency restoration. |
| `README.md` | This user and developer guide. Markdown is plain text with formatting. The application does not execute it. |
| `Test-results.txt` | Saved output from the completed checks. It does not run tests or automatically update when source code changes. |
| `Preview.png` | A rendered view of the interface processing a synthetic test recording. It is not used by the app. |

### Runnable files in `App`

| File | What it does |
|---|---|
| `VoiceFlow.exe` | Native Windows launcher; double-click this to start the application. |
| `VoiceFlow.dll` | Compiled application code loaded by .NET. |
| `VoiceFlow.runtimeconfig.json` | Requests the .NET 8 core and Windows desktop runtimes and holds generated runtime options. |
| `VoiceFlow.deps.json` | Describes the application's compiled dependencies, including `VoiceFlow.dll`. |
| `VoiceFlow.pdb` | Debugging symbols that connect compiled code to source locations. Normal operation does not require this file. |

Keep the executable, DLL, and JSON files together. The package is framework-dependent: the .NET Desktop Runtime is installed separately.

### Generated build folders

The working project may contain `bin` and `obj`. These are not included in the distributed ZIP. `bin` contains build output; `obj` contains intermediate build files.

| Generated file or pattern | Purpose |
|---|---|
| `project.assets.json` | Resolved framework/package dependency information. |
| `project.nuget.cache` | Cached dependency-restore information. |
| `VoiceFlow.csproj.nuget.dgspec.json` | Project specification used during dependency restoration. |
| `VoiceFlow.csproj.nuget.g.props` / `.targets` | Generated settings and instructions imported by the build. |
| `VoiceFlow.GlobalUsings.g.cs` | Automatically imports common C# namespaces. |
| `VoiceFlow.AssemblyInfo.cs` | Generated assembly name, version, and metadata. |
| `VoiceFlow.GeneratedMSBuildEditorConfig.editorconfig` | Build properties made available to compiler tools and analyzers. |
| `VoiceFlow.csproj.FileListAbsolute.txt` | Lists generated files for build/cleanup bookkeeping. |
| `PublishOutputs.*.txt` | Lists files produced by publishing. |
| `*.cache` | Records build state to help avoid unnecessary recompilation. |
| `apphost.exe` | Intermediate native application launcher. |
| `ref/VoiceFlow.dll`, `refint/VoiceFlow.dll` | Reference assemblies used during compilation. |
| Other `.dll` and `.pdb` files | Intermediate compiled code and debugging symbols. |

Edit the source files and project settings. The build tools regenerate these intermediate files.

## Understanding `Audio.cs`

The excerpts below show essential parts of the implementation. Some surrounding loops and error checks are omitted for readability; consult the source for the full methods.

### 1. Audio is an array of numbers

```csharp
public sealed record Audio(float[] Samples, int Rate, int Channels)
{
    public int Frames => Samples.Length / Channels;
    public double Seconds => Frames / (double)Rate;
}
```

- `Samples` holds waveform amplitudes, represented internally as floating-point numbers approximately between −1 and +1.
- `Rate` is the number of audio frames per second.
- `Channels` is 1 for mono or 2 for stereo.
- A frame contains one sample per channel. Stereo samples are interleaved: left, right, left, right.
- `=>` defines a calculated property. `Seconds` divides frames by sample rate.

One second of stereo audio at 48,000 Hz contains 48,000 frames and 96,000 sample values.

Other records group related information:

```csharp
public sealed record Cut(int Start, int End, string Kind);
public sealed record Result(
    Audio Audio, double PeakDb, double RmsDb,
    double GainDb, bool Limited);
```

`Cut` uses frame positions: `Start` is included and `End` is excluded. `Kind` describes the reason for the cut. `Settings` stores processing options. `Result` returns the processed audio, measurements, normalization gain, and whether peak protection capped that gain. A C# record is a convenient data container; the sample array itself remains mutable.

### 2. Reading WAV data

`Wave.Read()` opens the file, validates its RIFF/WAVE header, reads the `fmt ` chunk for format information, and locates the `data` chunk containing samples.

```csharp
string Id() => Encoding.ASCII.GetString(r.ReadBytes(4));

if (stream.Length < 12 || Id() != "RIFF")
    throw new InvalidDataException("Choose an uncompressed WAV file.");

r.ReadUInt32();
if (Id() != "WAVE")
    throw new InvalidDataException("This is not a WAV file.");
```

`BinaryReader` reads structured binary values. `throw` ends the operation with an error when the file is invalid. A `using` declaration ensures a file or stream is disposed when its scope ends.

For 16-bit PCM, decoding uses:

```csharp
value = r.ReadInt16() / 32768f;
```

A signed 16-bit value ranges from −32,768 to 32,767. Dividing by 32,768 converts it into the common internal range. For example, 16,384 becomes 0.5. Other supported bit depths are converted to the same representation.

### 3. Amplitude and decibels

```csharp
public static double Db(double value) =>
    20 * Math.Log10(Math.Max(value, 1e-10));

public static double Amp(double db) =>
    Math.Pow(10, db / 20);
```

The algorithms multiply amplitudes, while the controls display decibels. These helpers convert between the two. The small lower bound prevents taking the logarithm of zero.

| Amplitude | Approximate level |
|---|---|
| 1.0 | 0 dBFS |
| 0.5 | −6 dBFS |
| 0.1 | −20 dBFS |
| 0.01 | −40 dBFS |

dBFS means decibels relative to digital full scale.

### 4. Silence detection with RMS

`Dsp.Analyze()` returns suggested cuts. It examines windows of approximately 10 milliseconds:

```csharp
int hop = Math.Max(1, a.Rate / 100);
```

For each channel in a window, it adds squared sample values:

```csharp
double sum = 0;
for (int i = start; i < end; i++)
{
    double x = a.Samples[i * a.Channels + c];
    sum += x * x;
}
```

It selects the channel with the greatest energy and calculates RMS:

```csharp
rms[f] = Math.Sqrt(energy / (end - start));
```

RMS means root mean square: square the values, calculate their mean, and take the square root. Squaring prevents positive and negative waveform values from canceling. Using the stronger stereo channel helps protect speech present on only one side.

Consecutive windows below the silence threshold are grouped. Groups shorter than the minimum pause are ignored. Padding then reduces the proposed cut:

```csharp
int pad = s.PaddingMs * a.Rate / 1000;
int left = start == 0 ? 0 : start + pad;
int right = end == a.Frames ? end : end - pad;
if (right > left)
    cuts.Add(new Cut(left, right, "Silence"));
```

With 35 ms padding, an internal pause from 1.0 to 2.0 seconds produces a cut from 1.035 to 1.965 seconds. This protects speech edges. Silence detection measures volume; it does not understand words.

### 5. Breath detection with a heuristic

A heuristic is a set of practical rules. This detector looks for relatively quiet noise with substantial high-frequency energy near a pause.

```csharp
double x = a.Samples[i * a.Channels + dominant];
low += alpha * (x - low);
high += (x - low) * (x - low);
```

`low` is a smoothed version of the signal. Subtracting it from `x` emphasizes faster changes. `high` accumulates that difference's squared energy. The smoothing coefficient uses a nominal 1,800 Hz frequency:

```csharp
alpha = 1 - Math.Exp(-2 * Math.PI * 1800 / a.Rate);
```

Candidate windows must satisfy three conditions:

```csharp
double ceiling = Amp(-35 + s.BreathStrength * 0.15);
double whiteNoiseRatio =
    2 * (1 - alpha) * (1 - alpha) / (2 - alpha);

breath[f] =
    rms[f] > gate &&
    rms[f] < ceiling &&
    high / Math.Max(energy, 1e-12) / whiteNoiseRatio
        > 1.10 - s.BreathStrength * 0.005;
```

The signal must be above the silence gate, below the breath volume ceiling, and sufficiently rich in high-frequency energy. `whiteNoiseRatio` adjusts the comparison using the filter's expected response to white noise. Increasing sensitivity raises the volume ceiling and lowers the required energy ratio.

Consecutive candidate windows must last about 80–700 ms and be within approximately 150 ms of a quiet section. These rules can miss breaths and confuse them with consonants or background noise. The detector has no trained model or understanding of speech.

### 6. Merging cuts

`Dsp.Process()` sorts the chosen cuts and merges overlaps:

```csharp
if (merged.Count > 0 && cut.Start <= merged[^1].End)
    merged[^1] = merged[^1] with
    {
        End = Math.Max(merged[^1].End, cut.End)
    };
else
    merged.Add(cut);
```

`[^1]` means the last item. `with` copies a record while changing selected properties. Cuts covering 1.0–1.8 and 1.5–2.0 seconds become one cut covering 1.0–2.0 seconds.

The method identifies the sections outside those cuts and copies them into a new sample array. It rejects a selection that would remove the entire recording.

### 7. Smoothing joins with crossfades

```csharp
double t = (j + 1.0) / (overlap + 1);
samples[dst] = (float)(
    samples[dst] * (1 - t) +
    a.Samples[(segment.Start + j) * a.Channels + c] * t
);
```

As `t` increases, the outgoing audio contributes less and the incoming audio contributes more. This linear crossfade lasts up to 5 ms and reduces abrupt waveform jumps. Shorter segments use shorter overlaps. When cuts are applied, the recording boundaries also receive short fades of up to 3 ms.

The app removes time from gaps; it does not accelerate spoken words.

### 8. Compressing dynamics

The compressor follows amplitude with a smoothed envelope:

```csharp
double coeff = peak > envelope ? attack : release;
envelope = coeff * envelope + (1 - coeff) * peak;
```

It responds to rising levels with a 5 ms attack time constant and falling levels with a 100 ms release time constant. The peak is taken across channels, so both channels receive the same gain.

```csharp
double over = Math.Max(0, Db(envelope) - s.ThresholdDb);
double gain = Amp(-over * (1 - 1 / s.Ratio));

for (int c = 0; c < a.Channels; c++)
    samples[i * a.Channels + c] *= (float)gain;
```

`over` measures how far the detected level exceeds the threshold. At a 3:1 ratio, a steady detected level 12 dB above threshold receives 8 dB of gain reduction, leaving it approximately 4 dB above threshold. Attack and release affect the response during changes in level.

### 9. Normalizing RMS with peak protection

After compression, the method measures overall RMS and the largest absolute sample value.

```csharp
double requested = Amp(s.TargetDb) / rmsValue;
gainValue = Math.Min(requested, Amp(-1) / Math.Max(max, 1e-9));
```

The first gain would reach the requested RMS target. The second is the largest gain that keeps sample peaks at −1 dBFS. The smaller gain is applied uniformly:

```csharp
samples[i] *= (float)gainValue;
```

Compression changes gain over time. Normalization applies one gain to the entire processed recording. If peak protection limits the gain, the RMS target will not be reached. This implementation does not calculate LUFS or oversampled true peaks.

### 10. Exporting samples

`Wave.Write()` writes the WAV header and converts samples to 16-bit PCM:

```csharp
foreach (float s in audio.Samples)
    w.Write((short)Math.Clamp(
        Math.Round(s * 32768.0), -32768, 32767));
```

For example, 0.5 becomes 16,384. Rounding quantizes the result to an integer; clamping keeps it in range. Export does not apply dithering.

`Wave.Save()` writes a temporary file first, then moves it to the destination. The interface rejects the imported path as an export destination to preserve the original recording.

## Understanding `Program.cs`

### Startup

`Program` contains the entry point. The normal startup path is:

```csharp
ApplicationConfiguration.Initialize();
using var form = new MainForm();
Application.Run(form);
```

The first call configures Windows Forms. `MainForm` constructs the window. `Application.Run()` starts the message loop that handles clicks, keyboard input, and repainting. `[STAThread]` configures the main thread for Windows UI components.

Command-line branches run the audio tests, UI workflow test, or preview renderer instead of normal interactive operation.

### Building the window

`MainForm` creates controls directly in C#. There is no separate designer file. Table and flow layout panels arrange the controls, and helper methods create consistently styled buttons, checkboxes, and numeric inputs.

```csharp
files.Controls.Add(
    Button("Open WAV", async () => await Import(), true));
```

This creates a button and connects its action to `Import()`. The lambda `() => ...` is a small function passed as a value. `async` and `await` allow asynchronous work.

### Application state

```csharp
Audio? source, output;
List<Cut> candidates = new();
```

`source` holds original audio, `output` holds the current processed result, and `candidates` holds detected cuts. `?` allows a reference to be null, such as before loading a file.

`Options()` copies control values into a `Settings` record. `Import()` opens a file picker, calls `Wave.Read()` in the background, and updates the waveform and labels.

### Background processing and cancellation

```csharp
var finished = await Task.Run(() =>
{
    var cuts = analyze
        ? Dsp.Analyze(audio, options, cts.Token)
        : selected;
    return (Cuts: cuts,
        Result: Dsp.Process(audio, options, cuts, cts.Token));
}, cts.Token);
```

`Task.Run()` performs processing on a background thread so the window can remain responsive. With `analyze` true, the app detects cuts. With it false, the app uses the existing checked selections. The returned tuple contains both cuts and the processed result.

`CancellationTokenSource` lets Cancel request cancellation. Processing loops periodically check the token and stop with `OperationCanceledException`. Cancellation is cooperative rather than instantaneous. `SetBusy()` disables conflicting controls while work runs.

### Invalidating an outdated result

```csharp
void InvalidateOutput(string message)
{
    output = null;
    waveform.Processed = null;
    waveform.Invalidate();
    Stop();
    status.Text = message;
}
```

After settings or cut selections change, the previous output no longer represents the current choices. Clearing it prevents accidental playback or export of that outdated result. `Invalidate()` requests a redraw.

### Playing audio

```csharp
playback = new MemoryStream();
Wave.Write(playback, audio);
playback.Position = 0;
player = new SoundPlayer(playback);
player.Load();
player.Play();
```

The app creates WAV data in memory, rewinds the stream, and passes it to Windows playback. `Stop()` stops playback and disposes the player and stream. `HearCut()` extracts the selected original section with up to half a second of context on each side.

### Drawing waveforms

`WaveView` is a custom control. `OnPaint()` draws the original and, when available, processed signal. For each horizontal column, `Draw()` estimates the peak magnitude and draws a vertical line:

```csharp
g.DrawLine(pen, left + x, center - peak * half,
    left + x, center + peak * half);
```

Louder sections produce taller lines. Sampling the data keeps drawing fast, so this is an approximate overview rather than a sample-accurate editor. Each waveform scales its own duration to the available width. Double buffering reduces drawing flicker.

### Export and errors

`Export()` asks for a destination, rejects the original input path, and calls `Wave.Save()`. `Error()` displays an error message in the status label and a dialog. `SmokeTest()` exercises processing, cut review, result invalidation, reprocessing, and WAV export/readback using synthetic audio.

## Understanding `Tests.cs` and the test report

`Tests.Run()` generates tones, silence, and noise, then checks specific properties of the output. It records `PASS` lines, or a `FAIL` with an exception and nonzero exit code.

Example:

```csharp
var normalized = Dsp.Process(
    tone, off with { Normalize = true }, [], default);

Check(Math.Abs(normalized.RmsDb + 18) < .01,
    "RMS normalization reaches target");
```

This copies baseline settings, enables normalization, supplies an empty cut list (`[]`), and checks that RMS is within 0.01 dB of −18 dBFS. `default` supplies a cancellation token with no cancellation requested.

The 16 audio checks cover bypass preservation, RMS normalization, compression, stereo balance, peak protection, silence detection, padding, restoring unchecked cuts, overlapping cuts, basic breath heuristics, cancellation, whole-recording removal rejection, WAV roundtrip metadata, quantization accuracy, and malformed input. The separate UI check verifies the processing/review workflow.

The existing `Test-results.txt` records a successful run of these checks. Synthetic tests verify selected mechanics; they do not measure breath detection accuracy on real speech or establish audible quality.

To run checks from the project folder, use an existing writable folder for reports:

```powershell
New-Item -ItemType Directory -Force ./test-output | Out-Null
$reportDirectory = (Resolve-Path ./test-output).Path
dotnet ./App/VoiceFlow.dll --self-test "$reportDirectory/audio-tests.txt"
dotnet ./App/VoiceFlow.dll --ui-test "$reportDirectory/ui-tests.txt"
```

The UI check also saves a PNG beside its report. The audio test temporarily uses `roundtrip.wav` in the report directory, so use a dedicated test folder.

## Understanding configuration files

### `VoiceFlow.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <AssemblyName>VoiceFlow</AssemblyName>
  </PropertyGroup>
</Project>
```

- `Sdk` selects the standard .NET build tools.
- `WinExe` creates a Windows app without a normal console window.
- `TargetFramework` selects .NET 8 with Windows features.
- `UseWindowsForms` enables the desktop UI framework.
- `ImplicitUsings` imports common namespaces automatically.
- `Nullable` enables compiler checks for potentially missing references.
- `AssemblyName` names the compiled program.

The SDK includes the project's C# files automatically.

### `NuGet.Config`

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /></packageSources>
</configuration>
```

NuGet is .NET's package manager. `clear` removes inherited package sources for this project. This version builds against installed framework components and needs no third-party package downloads. The file configures building, not runtime network permissions.

### Runtime and dependency JSON

`VoiceFlow.runtimeconfig.json` requests `Microsoft.NETCore.App` and `Microsoft.WindowsDesktop.App`, both with a base version of `8.0.0`. Compatible installed patch versions can satisfy these requirements. Its additional generated properties configure runtime behavior; they do not control audio processing.

`VoiceFlow.deps.json` identifies `VoiceFlow.dll` as the project's runtime assembly. Both JSON files are generated by the build and should travel with the application.

## Suggested reading order

1. Read the workflow and controls in this guide.
2. Read the records at the top of `Audio.cs` to understand the data.
3. Read `Dsp.Analyze()` to understand detection.
4. Read `Dsp.Process()` to understand cuts and volume processing.
5. Read `Import()`, `Process()`, `Play()`, and `Export()` in `Program.cs` to connect the engine to the interface.
6. Read `Tests.cs` for concrete input/output examples.

The interface collects choices, the audio engine transforms samples, and the interface presents and saves the result.
