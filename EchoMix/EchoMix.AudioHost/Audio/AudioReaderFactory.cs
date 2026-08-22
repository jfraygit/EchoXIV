using System;
using System.IO;
using NAudio.Vorbis;
using NAudio.Wave;

namespace EchoMix.AudioHost.Audio;

/// Opens any of TrackImporter's supported formats as a WaveStream.
public static class AudioReaderFactory
{
    public static WaveStream Open(string filePath) =>
        Path.GetExtension(filePath).Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            ? new VorbisWaveReader(filePath)
            : new AudioFileReader(filePath);
}
