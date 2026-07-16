using System;
using Avalonia.Platform;
using NAudio.Vorbis;
using NAudio.Wave;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class EasterEggAudioService : IDisposable
{
    private readonly object syncRoot = new();
    private WaveOutEvent? output;
    private VorbisWaveReader? reader;
    private bool disposed;

    public void PlayKuyashi()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            DisposePlayback();

            var stream = AssetLoader.Open(
                new Uri("avares://Cafe.Launcher.Avalonia/Assets/kuyashi.ogg"));
            reader = new VorbisWaveReader(stream);
            output = new WaveOutEvent();
            output.PlaybackStopped += OnPlaybackStopped;
            output.Init(reader);
            output.Play();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (syncRoot)
        {
            DisposePlayback();
        }
    }

    private void DisposePlayback()
    {
        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
            output.Dispose();
            output = null;
        }

        reader?.Dispose();
        reader = null;
    }

    public void Dispose()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            DisposePlayback();
        }
    }
}
