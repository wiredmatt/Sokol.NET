// AudioManager.cs — OGG sound-effect player for BlockFall.
// Loads .ogg files via FileSystem, decodes with NVorbis, plays via saudio_push.

using System;
using System.Collections.Generic;
using NVorbis;
using Sokol;
using static Sokol.SAudio;

public enum SoundEffect { Clear, Impact, GameEnd }

public static class AudioManager
{
    // -----------------------------------------------------------------------
    // Internal state
    // -----------------------------------------------------------------------
    const int CHANNELS    = 2;   // stereo — matches our OGG files
    const int SAMPLE_RATE = 44100;
    const int MAX_VOICES  = 8;   // max simultaneously playing sounds

    static float[]?[] _samples    = new float[3][];   // decoded PCM per sound
    static bool[]     _loaded     = new bool[3];

    struct Voice
    {
        public int SoundId;
        public int Position;   // sample index into _samples[SoundId]
    }

    static readonly Voice[] _voices = new Voice[MAX_VOICES];
    static int _voiceCount = 0;
    static readonly object _lock = new object();

    static bool _audioReady = false;

    // -----------------------------------------------------------------------
    // Init — call once after sg_setup
    // -----------------------------------------------------------------------
    public static void Init()
    {
        saudio_setup(new saudio_desc
        {
            sample_rate  = SAMPLE_RATE,
            num_channels = CHANNELS,
            logger = { func = null },
        });

        _audioReady = saudio_isvalid();

        LoadSound("clear.ogg",   (int)SoundEffect.Clear);
        LoadSound("impact.ogg",  (int)SoundEffect.Impact);
        LoadSound("GameEnd.ogg", (int)SoundEffect.GameEnd);
    }

    // -----------------------------------------------------------------------
    // Shutdown — call in Cleanup()
    // -----------------------------------------------------------------------
    public static void Shutdown()
    {
        if (_audioReady)
            saudio_shutdown();
        _audioReady = false;
    }

    // -----------------------------------------------------------------------
    // PushAudio — call every Frame() to feed saudio
    // -----------------------------------------------------------------------
    static int _pushCount = 0;
    public static void PushAudio()
    {
        if (!_audioReady) return;

        int want = saudio_expect();
        _pushCount++;
        if (want <= 0) return;

        // Temporary mix buffer (stack-friendly for small want values)
        float[] buf = new float[want * CHANNELS];

        lock (_lock)
        {
            // --- SFX voices -----------------------------------------------
            for (int v = 0; v < _voiceCount; )
            {
                int sid = _voices[v].SoundId;
                float[]? src = _samples[sid];
                if (src == null || !_loaded[sid]) { v++; continue; }

                int pos = _voices[v].Position;
                int avail = src.Length - pos;
                int copy  = Math.Min(avail, buf.Length);

                for (int i = 0; i < copy; i++)
                    buf[i] = Math.Clamp(buf[i] + src[pos + i], -1f, 1f);

                pos += copy;
                if (pos >= src.Length)
                    _voices[v] = _voices[--_voiceCount];
                else
                {
                    _voices[v].Position = pos;
                    v++;
                }
            }

            // --- Music loop -----------------------------------------------
            if (_musicPlaying && _musicSamples != null && _musicSamples.Length > 0)
            {
                float[] msrc = _musicSamples;
                int mPos = _musicPos;
                for (int i = 0; i < buf.Length; i++)
                {
                    buf[i] = Math.Clamp(buf[i] + msrc[mPos] * MUSIC_VOLUME, -1f, 1f);
                    mPos++;
                    if (mPos >= msrc.Length) mPos = 0;   // loop seamlessly
                }
                _musicPos = mPos;
            }
        }

        // Push the mixed block
        saudio_push(in buf[0], want);
    }

    // -----------------------------------------------------------------------
    // Play — trigger a sound effect (can overlap)
    // -----------------------------------------------------------------------
    public static void Play(SoundEffect sfx)
    {
        if (!_audioReady) return;
        int sid = (int)sfx;
        if (!_loaded[sid]) return;

        lock (_lock)
        {
            if (_voiceCount >= MAX_VOICES) return;
            _voices[_voiceCount++] = new Voice { SoundId = sid, Position = 0 };
        }
    }

    /// <summary>Get or set whether the music loop is playing. Thread-safe.</summary>
    public static bool MusicEnabled
    {
        get { lock (_lock) { return _musicPlaying; } }
        set { lock (_lock) { _musicPlaying = value; } }
    }

    /// <summary>Stop all currently playing SFX voices immediately.</summary>
    public static void StopAllSfx()
    {
        lock (_lock) { _voiceCount = 0; }
    }

    // -----------------------------------------------------------------------
    // Music — looping background track (OGG, decoded once, looped in PushAudio)
    // -----------------------------------------------------------------------
    static float[]? _musicSamples;
    static int      _musicPos;
    static bool     _musicPlaying;
    const  float    MUSIC_VOLUME = 0.45f;   // softer than SFX

    /// <summary>Start the looping background music. File must be an OGG in Assets/.</summary>
    public static void PlayMusic(string filename)
    {
        FileSystem.Instance.LoadFile(filename, (filePath, data, status) =>
        {
            if (status != FileLoadStatus.Success || data == null) return;
            DecodeMusicOgg(data);
        });
    }

    static void DecodeMusicOgg(byte[] data)
    {
        try
        {
            using var ms     = new System.IO.MemoryStream(data);
            using var reader = new VorbisReader(ms);

            int channels = reader.Channels;
            long totalSamples = reader.TotalSamples;
            int outFrames = totalSamples > 0
                ? (int)totalSamples
                : (int)(reader.TotalTime.TotalSeconds * SAMPLE_RATE + 0.5);

            float[] interleaved = new float[outFrames * CHANNELS];
            float[] readBuf     = new float[4096 * channels];
            int writePos = 0;

            int framesRead;
            while ((framesRead = reader.ReadSamples(readBuf, 0, readBuf.Length)) > 0)
            {
                int frames = framesRead / channels;
                for (int f = 0; f < frames; f++)
                {
                    float L = readBuf[f * channels];
                    float R = channels > 1 ? readBuf[f * channels + 1] : L;
                    if (writePos + 1 < interleaved.Length)
                    {
                        interleaved[writePos]     = L;
                        interleaved[writePos + 1] = R;
                        writePos += 2;
                    }
                }
            }
            if (writePos < interleaved.Length)
                Array.Resize(ref interleaved, writePos);

            lock (_lock)
            {
                _musicSamples = interleaved;
                _musicPos     = 0;
                _musicPlaying = true;
            }
        }
        catch { }
    }

    // -----------------------------------------------------------------------
    // Private helpers
    // -----------------------------------------------------------------------
    static void LoadSound(string path, int soundId)
    {
        FileSystem.Instance.LoadFile(path, (filePath, data, status) =>
        {
            if (status != FileLoadStatus.Success || data == null)
            {
                // file not found — silently skip
                return;
            }

            DecodeOgg(data, soundId);
        });
    }

    static void DecodeOgg(byte[] data, int soundId)
    {
        try
        {
            using var ms     = new System.IO.MemoryStream(data);
            using var reader = new VorbisReader(ms);

            int channels   = reader.Channels;
            int sampleRate = (int)reader.SampleRate;
            long totalSamples = reader.TotalSamples;   // per-channel frames

            // We always want stereo @ 44100 Hz.
            // If the file differs, we do a simple re-sample/up-mix inline.
            int outFrames = (totalSamples > 0)
                ? (int)totalSamples
                : (int)(reader.TotalTime.TotalSeconds * SAMPLE_RATE + 0.5);

            float[] interleaved = new float[outFrames * CHANNELS];
            float[] readBuf     = new float[1024 * channels];
            int writePos = 0;

            int framesRead;
            while ((framesRead = reader.ReadSamples(readBuf, 0, readBuf.Length / channels * channels)) > 0)
            {
                int frames = framesRead / channels;
                for (int f = 0; f < frames; f++)
                {
                    float L = readBuf[f * channels];
                    float R = channels > 1 ? readBuf[f * channels + 1] : L;

                    if (writePos + 1 < interleaved.Length)
                    {
                        interleaved[writePos]     = L;
                        interleaved[writePos + 1] = R;
                        writePos += 2;
                    }
                }
            }

            // Trim to what was actually written
            if (writePos < interleaved.Length)
                Array.Resize(ref interleaved, writePos);

            lock (_lock)
            {
                _samples[soundId] = interleaved;
                _loaded[soundId]  = true;
            }
        }
        catch (Exception ex) { _ = ex; }
    }
}
