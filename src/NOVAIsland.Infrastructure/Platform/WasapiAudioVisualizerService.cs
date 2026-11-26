using NAudio.Dsp;
using NAudio.Wave;
using NOVAIsland.Core.Interfaces;

namespace NOVAIsland.Infrastructure.Platform;

/// <summary>
/// Real-time WASAPI loopback FFT spectrum analyzer feeding the Dynamic Island AudioEqualizer.
/// </summary>
public sealed class WasapiAudioVisualizerService : IAudioVisualizerService, IDisposable
{
    private const int FftLength = 1024;
    private readonly Complex[] _fftBuffer = new Complex[FftLength];
    private readonly float[] _bands = new float[8];
#pragma warning disable CS0618 // WasapiLoopbackCapture remains functional for system-wide loopback across Win10 19041+
    private WasapiLoopbackCapture? _capture;
    private int _fftPos;
    private long _lastEmitTicks;

    public event EventHandler<float[]>? SpectrumUpdated;

    public void Start()
    {
        if (_capture is not null)
        {
            return;
        }

        try
        {
            _capture = new WasapiLoopbackCapture();
            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += (_, _) => { };
            _capture.StartRecording();
        }
        catch
        {
            _capture?.Dispose();
            _capture = null;
        }
    }

    public void Stop()
    {
        if (_capture is null)
        {
            return;
        }

        try
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.StopRecording();
        }
        catch { }
        finally
        {
            _capture.Dispose();
            _capture = null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded == 0 || _capture is null)
        {
            return;
        }

        var channels = Math.Max(1, _capture.WaveFormat.Channels);
        var bytesPerSample = Math.Max(4, _capture.WaveFormat.BitsPerSample / 8);
        var step = channels * bytesPerSample;

        for (var offset = 0; offset + bytesPerSample <= e.BytesRecorded; offset += step)
        {
            var sample = BitConverter.ToSingle(e.Buffer, offset);
            _fftBuffer[_fftPos].X = (float)(sample * FastFourierTransform.HammingWindow(_fftPos, FftLength));
            _fftBuffer[_fftPos].Y = 0;
            _fftPos++;

            if (_fftPos >= FftLength)
            {
                _fftPos = 0;
                FastFourierTransform.FFT(true, 10, _fftBuffer);
                EmitBandsIfDue();
            }
        }
    }

    private void EmitBandsIfDue()
    {
        var now = Environment.TickCount64;
        if (now - _lastEmitTicks < 60)
        {
            return;
        }
        _lastEmitTicks = now;

        // Logarithmic bin ranges across audible music frequencies
        int[] binStarts = [2, 5, 10, 20, 40, 80, 150, 260];
        int[] binEnds = [5, 10, 20, 40, 80, 150, 260, 460];

        for (var b = 0; b < 8; b++)
        {
            var sum = 0f;
            var count = binEnds[b] - binStarts[b];
            for (var bin = binStarts[b]; bin < binEnds[b]; bin++)
            {
                var mag = MathF.Sqrt(_fftBuffer[bin].X * _fftBuffer[bin].X + _fftBuffer[bin].Y * _fftBuffer[bin].Y);
                sum += mag;
            }

            var avg = sum / Math.Max(1, count);
            var normalized = Math.Clamp(MathF.Log10(1 + avg * 650f) * 0.72f, 0.16f, 1.0f);
            _bands[b] = _bands[b] * 0.45f + normalized * 0.55f;
        }

        SpectrumUpdated?.Invoke(this, _bands.ToArray());
    }

    public void Dispose() => Stop();
}
