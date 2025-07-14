using FFmpeg.AutoGen;
using PenguinTools.Common;
using System.Globalization;
using System.Text;

namespace PenguinMedia.Audio;

public struct AudioStreamInfo
{
    public int StreamIndex;
    public string? CodecName;
    public int SampleRate;
    public int Channels;
    public double IntegratedLoudness;
    public double TruePeak;
}

public static unsafe class FFmpegUtils
{
    public static void Initialize(bool verbose = false)
    {
        ffmpeg.av_log_set_level(verbose ? ffmpeg.AV_LOG_VERBOSE : ffmpeg.AV_LOG_ERROR);
    }

    private static AVFormatContext* OpenBestAudioStream(string filePath, out AVStream* stream)
    {
        if (string.IsNullOrWhiteSpace(filePath)) throw new MediaException(Strings.Media_Error_path_null_or_empty);

        AVFormatContext* fmt = null;
        Check(ffmpeg.avformat_open_input(&fmt, filePath, null, null));
        try
        {
            Check(ffmpeg.avformat_find_stream_info(fmt, null));
            var idx = ffmpeg.av_find_best_stream(fmt, AVMediaType.AVMEDIA_TYPE_AUDIO, -1, -1, null, 0);
            if (idx < 0) throw new MediaException(Strings.Media_Error_file_no_audio_stream_available);

            stream = fmt->streams[idx];
            return fmt;
        }
        catch
        {
            ffmpeg.avformat_close_input(&fmt);
            throw;
        }
    }

    private static AVCodecContext* OpenDecoder(AVStream* stream)
    {
        var codec = ffmpeg.avcodec_find_decoder(stream->codecpar->codec_id);
        if (codec == null) throw new MediaException(Strings.Media_Error_no_avaliable_acodec);

        var ctx = Ensure(ffmpeg.avcodec_alloc_context3(codec));
        try
        {
            Check(ffmpeg.avcodec_parameters_to_context(ctx, stream->codecpar));
            Check(ffmpeg.avcodec_open2(ctx, codec, null));
            return ctx;
        }
        catch
        {
            ffmpeg.avcodec_free_context(&ctx);
            throw;
        }
    }

    private static void ReceiveFrames(AVCodecContext* dec, AVFrame* frm, AVFilterContext* src, AVFilterContext* sink, AVFrame* filt)
    {
        for (;;)
        {
            var ret = ffmpeg.avcodec_receive_frame(dec, frm);
            if (ret == ffmpeg.AVERROR(ffmpeg.EAGAIN) || ret == ffmpeg.AVERROR_EOF) break;
            Check(ret);

            Check(ffmpeg.av_buffersrc_add_frame(src, frm));
            ffmpeg.av_frame_unref(frm);

            while (ffmpeg.av_buffersink_get_frame(sink, filt) == 0) ffmpeg.av_frame_unref(filt);
        }
    }

    private static void Check(int error)
    {
        if (error < 0) throw new MediaException($"FFmpeg failed: {GetErrorMessage(error)}");
    }

    private static T* Ensure<T>(T* ptr) where T : unmanaged
    {
        return ptr != null ? ptr : throw new MediaException($"FFmpeg failed: {typeof(T).Name} allocation failed");
    }

    private static AVFilterContext* CreateFilter(AVFilterGraph* g, string name, string instance)
    {
        var p = ffmpeg.avfilter_graph_alloc_filter(g, ffmpeg.avfilter_get_by_name(name), instance);
        return Ensure(p);
    }

    private static AVFilterContext* CreateLinked(AVFilterGraph* g, AVFilterContext* from, string name, string label, string? args = null)
    {
        var ctx = CreateFilter(g, name, label);
        Check(ffmpeg.avfilter_init_str(ctx, args));
        Check(ffmpeg.avfilter_link(from, 0, ctx, 0));
        return ctx;
    }

    private static AVFilterContext* CreateBuffer(AVFilterGraph* graph, AVCodecContext* codec)
    {
        AVBufferSrcParameters* par = null;
        try
        {
            var src = CreateFilter(graph, "abuffer", "in");
            par = Ensure(ffmpeg.av_buffersrc_parameters_alloc());
            par->format = (int)codec->sample_fmt;
            par->sample_rate = codec->sample_rate;
            ffmpeg.av_channel_layout_copy(&par->ch_layout, &codec->ch_layout);
            par->time_base = codec->time_base;
            Check(ffmpeg.av_buffersrc_parameters_set(src, par));
            Check(ffmpeg.avfilter_init_str(src, null));
            return src;
        }
        finally
        {
            ffmpeg.av_freep(&par);
        }
    }

    private static string GetErrorMessage(int err)
    {
        const int bufferSize = 1024;
        var buf = stackalloc byte[bufferSize];
        ffmpeg.av_strerror(err, buf, bufferSize);
        return Encoding.UTF8.GetString(buf, bufferSize).TrimEnd('\0');
    }

    public static AudioStreamInfo Probe(string srcPath, bool analyzeLoudness = true)
    {
        AVFormatContext* fmt = null;
        AVCodecContext* dec = null;
        AVFilterGraph* graph = null;
        AVBufferSrcParameters* par = null;
        AVPacket* pkt = null;
        AVFrame* frm = null, filt = null;

        try
        {
            fmt = OpenBestAudioStream(srcPath, out var st);
            dec = OpenDecoder(st);

            AudioStreamInfo info = new()
            {
                StreamIndex = st->index,
                CodecName = ffmpeg.avcodec_get_name(dec->codec_id),
                SampleRate = dec->sample_rate,
                Channels = dec->ch_layout.nb_channels
            };

            if (!analyzeLoudness) return info;

            graph = Ensure(ffmpeg.avfilter_graph_alloc());

            if (dec->ch_layout.order == AVChannelOrder.AV_CHANNEL_ORDER_UNSPEC || dec->ch_layout.nb_channels == 0)
            {
                var channels = dec->ch_layout.nb_channels;
                if (channels == 0) channels = st->codecpar->ch_layout.nb_channels;
                if (channels == 0) throw new MediaException(Strings.Media_Error_no_audio_channels_available);
                ffmpeg.av_channel_layout_uninit(&dec->ch_layout);
                ffmpeg.av_channel_layout_default(&dec->ch_layout, channels);
            }

            var src = CreateBuffer(graph, dec);
            var ebur = CreateLinked(graph, src, "ebur128", "ebur128", "peak=true:framelog=quiet");
            var sink = CreateLinked(graph, ebur, "abuffersink", "out");

            Check(ffmpeg.avfilter_graph_config(graph, null));

            pkt = Ensure(ffmpeg.av_packet_alloc());
            frm = Ensure(ffmpeg.av_frame_alloc());
            filt = Ensure(ffmpeg.av_frame_alloc());

            while (ffmpeg.av_read_frame(fmt, pkt) >= 0)
            {
                if (pkt->stream_index != st->index)
                {
                    ffmpeg.av_packet_unref(pkt);
                    continue;
                }

                Check(ffmpeg.avcodec_send_packet(dec, pkt));
                ffmpeg.av_packet_unref(pkt);

                ReceiveFrames(dec, frm, src, sink, filt);
            }

            Check(ffmpeg.avcodec_send_packet(dec, null));
            ReceiveFrames(dec, frm, src, sink, filt);

            Check(ffmpeg.av_buffersrc_add_frame(src, null));
            while (ffmpeg.av_buffersink_get_frame(sink, filt) == 0) ffmpeg.av_frame_unref(filt);

            Check(ffmpeg.av_opt_get_double(ebur->priv, "integrated", 0, &info.IntegratedLoudness));
            Check(ffmpeg.av_opt_get_double(ebur->priv, "true_peak", 0, &info.TruePeak));

            return info;
        }
        finally
        {
            ffmpeg.av_frame_free(&filt);
            ffmpeg.av_frame_free(&frm);
            ffmpeg.av_packet_free(&pkt);

            ffmpeg.avfilter_graph_free(&graph);
            ffmpeg.av_freep(&par);

            ffmpeg.avcodec_free_context(&dec);
            ffmpeg.avformat_close_input(&fmt);
        }
    }

    public static string Convert(string srcPath, double offset)
    {
        const double targetLoudness = -8.0;
        const double targetGainTolerance = 0.5;
        const double targetMaxTruePeak = -1.0;
        const int lookaheadMs = 10;
        const int releaseMs = 150;

        const AVSampleFormat targetSampleFmt = AVSampleFormat.AV_SAMPLE_FMT_S16;
        const int targetSampleRate = 48_000;
        const int targetChannels = 2;
        const string targetCodecName = "pcm_s16le";

        var info = Probe(srcPath);
        var gain = targetLoudness - info.IntegratedLoudness;
        var needVolume = Math.Abs(gain) > targetGainTolerance;
        var needLimiter = info.TruePeak > targetMaxTruePeak;
        var needOffset = Math.Abs(offset) > 0.000_001;
        var needReformat = info.SampleRate != targetSampleRate || info.CodecName != targetCodecName || info.Channels != targetChannels;
        if (!(needVolume || needLimiter || needOffset || needReformat)) return srcPath;

        var dstPath = ResourceUtils.GetTempPath($"c_{Path.GetFileNameWithoutExtension(srcPath)}.wav");

        AVFormatContext* inFmt = null, outFmt = null;
        AVCodecContext* dec = null, enc = null;
        AVFilterGraph* graph = null;
        AVPacket* pkt = null;
        AVFrame* frm = null, filt = null;

        try
        {
            inFmt = OpenBestAudioStream(srcPath, out var inSt);
            dec = OpenDecoder(inSt);

            Check(ffmpeg.avformat_alloc_output_context2(&outFmt, null, "wav", dstPath));
            Ensure(outFmt);

            var outSt = ffmpeg.avformat_new_stream(outFmt, null);
            Ensure(outSt);

            enc = Ensure(ffmpeg.avcodec_alloc_context3(null));
            enc->codec_type = AVMediaType.AVMEDIA_TYPE_AUDIO;
            enc->codec_id = AVCodecID.AV_CODEC_ID_PCM_S16LE;
            enc->sample_rate = targetSampleRate;
            ffmpeg.av_channel_layout_default(&enc->ch_layout, targetChannels);
            enc->sample_fmt = targetSampleFmt;
            enc->bit_rate = 0;
            enc->time_base = new AVRational
            {
                num = 1,
                den = targetSampleRate
            };

            Check(ffmpeg.avcodec_open2(enc, ffmpeg.avcodec_find_encoder(enc->codec_id), null));
            Check(ffmpeg.avcodec_parameters_from_context(outSt->codecpar, enc));
            outSt->time_base = enc->time_base;

            if ((outFmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0) Check(ffmpeg.avio_open(&outFmt->pb, dstPath, ffmpeg.AVIO_FLAG_WRITE));
            Check(ffmpeg.avformat_write_header(outFmt, null));

            graph = Ensure(ffmpeg.avfilter_graph_alloc());

            var src = CreateBuffer(graph, dec);
            var last = src;
            if (needOffset)
            {
                if (offset > 0)
                {
                    var d = (int)Math.Round(offset * 1000.0);
                    last = CreateLinked(graph, last, "adelay", "del", $"delays={d}|{d}:all=1");
                }
                else
                {
                    var s = Math.Abs(offset);
                    last = CreateLinked(graph, last, "atrim", "trim", $"start={s.ToString(CultureInfo.InvariantCulture)}");
                }
            }
            if (needVolume) last = CreateLinked(graph, last, "volume", "vol", $"volume={gain.ToString(CultureInfo.InvariantCulture)}dB");
            if (needLimiter) last = CreateLinked(graph, last, "alimiter", "lim", $"level={targetMaxTruePeak}:lookahead={lookaheadMs}:release={releaseMs}");
            if (needReformat) last = CreateLinked(graph, last, "aformat", "fmt", $"sample_fmts={ffmpeg.av_get_sample_fmt_name(targetSampleFmt)}:sample_rates={targetSampleRate}:channel_layouts=stereo");
            var sink = CreateLinked(graph, last, "abuffersink", "out");

            Check(ffmpeg.avfilter_graph_config(graph, null));

            pkt = Ensure(ffmpeg.av_packet_alloc());
            frm = Ensure(ffmpeg.av_frame_alloc());
            filt = Ensure(ffmpeg.av_frame_alloc());

            while (ffmpeg.av_read_frame(inFmt, pkt) >= 0)
            {
                if (pkt->stream_index != inSt->index)
                {
                    ffmpeg.av_packet_unref(pkt);
                    continue;
                }

                Check(ffmpeg.avcodec_send_packet(dec, pkt));
                ffmpeg.av_packet_unref(pkt);

                Pump(dec, src, sink, frm, filt, enc, outFmt, outSt, pkt, inSt);
            }

            Check(ffmpeg.avcodec_send_packet(dec, null));
            Pump(dec, src, sink, frm, filt, enc, outFmt, outSt, pkt, inSt);

            Check(ffmpeg.av_buffersrc_add_frame(src, null));
            while (ffmpeg.av_buffersink_get_frame(sink, filt) == 0)
            {
                EncodeWrite(filt, enc, outFmt, outSt, pkt, inSt);
                ffmpeg.av_frame_unref(filt);
            }

            Check(ffmpeg.avcodec_send_frame(enc, null));
            while (ffmpeg.avcodec_receive_packet(enc, pkt) == 0)
            {
                WritePacket(pkt, enc, outFmt, outSt);
                ffmpeg.av_packet_unref(pkt);
            }

            Check(ffmpeg.av_write_trailer(outFmt));

            return dstPath;
        }
        finally
        {
            ffmpeg.av_frame_free(&filt);
            ffmpeg.av_frame_free(&frm);
            ffmpeg.av_packet_free(&pkt);

            ffmpeg.avfilter_graph_free(&graph);

            ffmpeg.avcodec_free_context(&dec);
            ffmpeg.avformat_close_input(&inFmt);

            ffmpeg.avcodec_free_context(&enc);
            if (outFmt != null && (outFmt->oformat->flags & ffmpeg.AVFMT_NOFILE) == 0) ffmpeg.avio_closep(&outFmt->pb);
            ffmpeg.avformat_free_context(outFmt);
        }

        static void Pump(AVCodecContext* decCtx, AVFilterContext* srcCtx, AVFilterContext* sinkCtx, AVFrame* decFrm, AVFrame* filtFrm, AVCodecContext* encCtx, AVFormatContext* outFmtCtx, AVStream* outStream, AVPacket* pkt, AVStream* inSt)
        {
            while (ffmpeg.avcodec_receive_frame(decCtx, decFrm) == 0)
            {
                Check(ffmpeg.av_buffersrc_add_frame(srcCtx, decFrm));
                ffmpeg.av_frame_unref(decFrm);

                while (ffmpeg.av_buffersink_get_frame(sinkCtx, filtFrm) == 0)
                {
                    EncodeWrite(filtFrm, encCtx, outFmtCtx, outStream, pkt, inSt);
                    ffmpeg.av_frame_unref(filtFrm);
                }
            }
        }

        static void EncodeWrite(AVFrame* frame, AVCodecContext* encCtx, AVFormatContext* outFmtCtx, AVStream* outStream, AVPacket* pkt, AVStream* inSt)
        {
            if (frame->pts != ffmpeg.AV_NOPTS_VALUE) frame->pts = ffmpeg.av_rescale_q(frame->pts, inSt->time_base, encCtx->time_base);

            Check(ffmpeg.avcodec_send_frame(encCtx, frame));
            while (ffmpeg.avcodec_receive_packet(encCtx, pkt) == 0)
            {
                WritePacket(pkt, encCtx, outFmtCtx, outStream);
                ffmpeg.av_packet_unref(pkt);
            }
        }

        static void WritePacket(AVPacket* p, AVCodecContext* encCtx, AVFormatContext* outFmtCtx, AVStream* outStream)
        {
            p->stream_index = outStream->index;
            p->pts = ffmpeg.av_rescale_q(p->pts, encCtx->time_base, outStream->time_base);
            p->dts = p->pts;
            p->duration = ffmpeg.av_rescale_q(p->duration, encCtx->time_base, outStream->time_base);
            Check(ffmpeg.av_interleaved_write_frame(outFmtCtx, p));
        }
    }
}