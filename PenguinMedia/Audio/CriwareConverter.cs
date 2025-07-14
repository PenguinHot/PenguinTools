using PenguinTools.Common;
using SonicAudioLib.Archives;
using SonicAudioLib.CriMw;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using VGAudio.Codecs.CriHca;
using VGAudio.Containers.Hca;
using VGAudio.Containers.Wave;
using VGAudio.Formats;

namespace PenguinMedia.Audio;
/*
 * Originally by Margrithm
 * https://margrithm.girlsband.party/
 */

public class CriwareConverter
{
    private readonly IAudioFormat waveData;

    public CriwareConverter(string wavePath, string cueName, string acbPath, string awbPath, double loopStart, double loopEnd)
    {
        var waveReader = new WaveReader();

        if (waveReader.ReadFormat(wavePath) is not { ChannelCount: 2, SampleRate: 48000 } wave)
        {
            throw new NotSupportedException(Strings.Error_audio_format_not_supported);
        }

        waveData = wave;
        CueName = cueName;
        AcbPath = acbPath;
        AwbPath = awbPath;
        LoopStart = loopStart;
        LoopEnd = loopEnd;
    }

    public ulong Key { get; set; } = 32931609366120192UL;
    public string CueName { get; set; }
    public string AcbPath { get; set; }
    public string AwbPath { get; set; }
    public double LoopStart { get; set; }
    public double LoopEnd { get; set; }

    public void Process()
    {
        var hcaWriter = new HcaWriter();
        var config = new HcaConfiguration
        {
            Bitrate = 16384 * 8,
            Quality = CriHcaQuality.Highest,
            TrimFile = false,
            EncryptionKey = new CriHcaKey(Key)
        };

        using var hcaMs = new MemoryStream();
        hcaWriter.WriteToStream(waveData, hcaMs, config);
        hcaMs.Seek(0, SeekOrigin.Begin);

        var cueSheetTable = new CriTable();

        cueSheetTable.Load(ResourceUtils.GetStream("dummy.acb"));
        cueSheetTable.Rows[0]["Name"] = CueName;

        var cueTable = new CriTable();
        cueTable.Load(cueSheetTable.Rows[0]["CueTable"] as byte[]);

        var lengthMs = (int)(waveData.SampleCount / (double)waveData.SampleRate * 1000.0);
        cueTable.Rows[0]["Length"] = lengthMs;

        cueTable.WriterSettings = CriTableWriterSettings.Adx2Settings;
        cueSheetTable.Rows[0]["CueTable"] = cueTable.Save();

        var trackEventTable = new CriTable();
        trackEventTable.Load(cueSheetTable.Rows[0]["TrackEventTable"] as byte[]);

        var cmdData = trackEventTable.Rows[1]["Command"] as byte[];
        var cmdStream = new MemoryStream(cmdData!);
        using (var bw = new BinaryWriter(cmdStream, Encoding.Default, true))
        {
            cmdStream.Position = 3;
            bw.WriteUInt32BigEndian((uint)(LoopStart * 1000.0));
            cmdStream.Position = 17;
            bw.WriteUInt32BigEndian((uint)(LoopEnd * 1000.0));
        }
        trackEventTable.Rows[1]["Command"] = cmdStream.ToArray();
        cueSheetTable.Rows[0]["TrackEventTable"] = trackEventTable.Save();

        var awbEntry = new CriAfs2Entry
        {
            Stream = hcaMs
        };
        var awbArchive = new CriAfs2Archive
        {
            awbEntry
        };
        using var awbStream = File.Create(AwbPath);
        awbArchive.Save(awbStream);
        awbStream.Position = 0;

        var streamAwbHashTbl = new CriTable();
        streamAwbHashTbl.Load(cueSheetTable.Rows[0]["StreamAwbHash"] as byte[]);

        var sha = SHA1.HashDataAsync(awbStream);
        streamAwbHashTbl.Rows[0]["Name"] = CueName;
        streamAwbHashTbl.Rows[0]["Hash"] = sha;
        cueSheetTable.Rows[0]["StreamAwbHash"] = streamAwbHashTbl.Save();

        var waveformTable = new CriTable();
        waveformTable.Load(cueSheetTable.Rows[0]["WaveformTable"] as byte[]);

        waveformTable.Rows[0]["SamplingRate"] = (ushort)waveData.SampleRate;
        waveformTable.Rows[0]["NumSamples"] = waveData.SampleCount;
        cueSheetTable.Rows[0]["WaveformTable"] = waveformTable.Save();

        cueSheetTable.WriterSettings = CriTableWriterSettings.Adx2Settings;
        using var acbStream = File.Create(AcbPath);
        cueSheetTable.Save(acbStream);
    }
}

public static class BinaryWriterExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteUInt32BigEndian(this BinaryWriter bw, uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        bw.Write(buffer);
    }
}