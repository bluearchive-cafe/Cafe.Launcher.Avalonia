using System;
using System.IO;
using Avalonia.Platform;
using Cafe.Launcher.Avalonia.Services.Diagnostics;
using NAudio.Vorbis;
using NAudio.Wave;

namespace Cafe.Launcher.Avalonia.Services;

public sealed class EasterEggAudioService : IDisposable
{
    private readonly object syncRoot = new();
    private readonly LocalDiagnostics? diagnostics;
    private readonly Action? playbackAction;
    private WaveOutEvent? output;
    private VorbisWaveReader? reader;
    private bool disposed;

    public EasterEggAudioService(LocalDiagnostics diagnostics)
    {
        this.diagnostics = diagnostics;
    }

    internal EasterEggAudioService(Action playbackAction)
    {
        this.playbackAction = playbackAction;
    }

    public void PlayKuyashi()
    {
        if (playbackAction is null && !OperatingSystem.IsWindows())
        {
            return;
        }

        lock (syncRoot)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            TryDisposePlayback();

            Stream? stream = null;
            try
            {
                if (playbackAction is not null)
                {
                    playbackAction();
                    return;
                }

                stream = AssetLoader.Open(
                    new Uri("avares://Cafe.Launcher.Avalonia/Assets/kuyashi.ogg"));
                reader = new VorbisWaveReader(stream);
                stream = null;
                output = new WaveOutEvent();
                output.PlaybackStopped += OnPlaybackStopped;
                output.Init(reader);
                output.Play();
            }
            catch (Exception exception)
            {
                stream?.Dispose();
                TryDisposePlayback();
                diagnostics?
                    .ErrorAsync("Easter egg audio playback failed.", exception)
                    .GetAwaiter()
                    .GetResult();
            }
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        lock (syncRoot)
        {
            TryDisposePlayback();
        }
    }

    private void TryDisposePlayback()
    {
        try
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
        catch (Exception exception)
        {
            output = null;
            reader = null;
            diagnostics?
                .ErrorAsync("Easter egg audio cleanup failed.", exception)
                .GetAwaiter()
                .GetResult();
        }
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
            TryDisposePlayback();
        }
    }
}
