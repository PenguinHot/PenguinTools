﻿using PenguinTools.Core.Resources;

namespace PenguinTools.Core.Chart.Parser;

using mg = Models.mgxc;

public partial class MgxcParser
{
    private void ParseEvent(BinaryReader br)
    {
        var name = br.ReadUtf8String(4);
        mg.Event? e = null;

        if (name == "beat")
        {
            e = new mg.BeatEvent
            {
                Bar = (int)br.ReadData(),
                Numerator = (int)br.ReadData(),
                Denominator = (int)br.ReadData()
            };
        }
        else if (name == "bpm ")
        {
            e = new mg.BpmEvent
            {
                Tick = (int)br.ReadData(),
                Bpm = br.ReadData().Round()
            };
        }
        else if (name == "smod")
        {
            e = new mg.NoteSpeedEvent
            {
                Tick = (int)br.ReadData(),
                Speed = br.ReadData().Round()
            };
        }
        else if (name == "til ")
        {
            e = new mg.ScrollSpeedEvent
            {
                Timeline = (int)br.ReadData(),
                Tick = (int)br.ReadData(),
                Speed = br.ReadData().Round()
            };
        }
        else if (name == "bmrk")
        {
            br.ReadBigData(); // hash
            e = new mg.BookmarkEvent
            {
                Tick = (int)br.ReadData(),
                Tag = (string)br.ReadBigData()
            };
            br.ReadBigData(); // rgb
        }
        else if (name == "mbkm")
        {
            e = new mg.BreakingMarker
            {
                Tick = (int)br.ReadData()
            };
        }

        if (e == null)
        {
            var msg = string.Format(Strings.Error_Unrecognized_event, name, br.BaseStream.Position);
            throw new DiagnosticException(msg, Mgxc);
        }

        Mgxc.Events.AppendChild(e);
        br.ReadInt32(); // 00 00 00 00
    }
}