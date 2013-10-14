/*****************************************************************************
 * Version: 0.1
 * License: GNU GPLv2
 * Authors: vocho
 *****************************************************************************/

using System;
using System.IO;
using System.Collections.Generic;
using System.Text.RegularExpressions;

class tstrimmer
{
    public static void Main(string[] args)
    {
        String input_file = "";
        String output_file = "";
        String trim_fps = "30000/1001";
        String trim_file = "";
        String trim_start_margin = "00:00:01.000";
        String trim_end_margin = "00:00:00.500";
        String seek_start = "";
        String seek_duration = "";
        int chunk_power = 14; // 10:188*1024, 12:188*4096, 14:188*16384, 16:188*65536

        if (args.Length == 0)
        {
            Console.WriteLine("tstrimmer -i in.ts -o out.ts [-trimfps " + trim_fps + "] [-trimfile in.ts.txt] [-trimsm hh:mm:ss.zzz] [-trimem hh:mm:ss.zzz] [-ss hh:mm:ss.zzz] [-t hh:mm:ss.zzz]");
            Console.WriteLine("    -i          input file name");
            Console.WriteLine("    -o          output file name");
            Console.WriteLine("    -trimfps    trim frame rate (defalut: " + trim_fps + ")");
            Console.WriteLine("    -trimfile   trim file name");
            Console.WriteLine("    -trimsm     trim start margin (defalut: " + trim_start_margin + ")");
            Console.WriteLine("    -trimem     trim end margin (defalut: " + trim_end_margin + ")");
            Console.WriteLine("    -ss         seek start time");
            Console.WriteLine("    -t          seek duration");
            Environment.Exit(0);
        }

        FileStream reader = null;
        FileStream writer = null;

        try
        {
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                switch (args[i])
                {
                    case "-i":
                        input_file = args[i + 1];
                        break;
                    case "-o":
                        output_file = args[i + 1];
                        break;
                    case "-trimfps":
                        trim_fps = args[i + 1];
                        break;
                    case "-trimfile":
                        trim_file = args[i + 1];
                        break;
                    case "-trimsm":
                        trim_start_margin = args[i + 1];
                        break;
                    case "-trimem":
                        trim_end_margin = args[i + 1];
                        break;
                    case "-ss":
                        seek_start = args[i + 1];
                        break;
                    case "-t":
                        seek_duration = args[i + 1];
                        break;
                }
            }


            // -i
            reader = new FileStream(input_file, FileMode.Open, FileAccess.Read);

            // -o
            writer = new FileStream(output_file, FileMode.Create, FileAccess.Write);

            // -trimfps
            uint trim_fps_num = 30000;
            uint trim_fps_den = 1001;
            if (trim_fps != "")
            {
                string[] ary = trim_fps.Split(new char[] { '/', ':', ',' });
                if (ary.Length == 2)
                {
                    trim_fps_num = uint.Parse(ary[0]);
                    trim_fps_den = uint.Parse(ary[1]);
                }
            }

            // -trimfile
            List<ulong> trim_pts_list = new List<ulong>();
            if (trim_file != "")
            {
                string trim_text = File.ReadAllText(trim_file);
                MatchCollection matches = Regex.Matches(trim_text, @"\bTrim\(\s*(?<start>\d+)\s*,\s*(?<end>\d+)\s*\)");
                foreach (Match match in matches)
                {
                    trim_pts_list.Add(ulong.Parse(match.Groups["start"].Value) * 90000 * trim_fps_den / trim_fps_num % 0x200000000);
                    trim_pts_list.Add(ulong.Parse(match.Groups["end"  ].Value) * 90000 * trim_fps_den / trim_fps_num % 0x200000000);
                }
            }
            if (trim_pts_list.Count == 0)
            {
                trim_pts_list.Add(0);
                trim_pts_list.Add(0x1FFFFFFFF);
            }

            // -ss
            if (seek_start != "")
            {
                ulong seek_start_pts = (ulong)(TimeSpan.Parse(seek_start).TotalSeconds * 90000) % 0x200000000;
                while ((0 < trim_pts_list.Count) && (0 < seek_start_pts))
                {
                    ulong diff = trim_pts_list[1] - trim_pts_list[0];
                    if (seek_start_pts <= diff)
                    {
                        trim_pts_list[0] += seek_start_pts;
                        break;
                    }
                    else
                    {
                        trim_pts_list.RemoveRange(0, 2);
                        seek_start_pts -= diff;
                    }
                }
            }

            // -t
            if (seek_duration != "")
            {
                ulong seek_duration_pts = (ulong)(TimeSpan.Parse(seek_duration).TotalSeconds * 90000) % 0x200000000;
                for (int i = 0; i < trim_pts_list.Count; i += 2)
                {
                    ulong diff = trim_pts_list[i + 1] - trim_pts_list[i];
                    if (seek_duration_pts <= diff)
                    {
                        trim_pts_list[i + 1] = trim_pts_list[i] + seek_duration_pts;
                        i += 2;
                        if (i < trim_pts_list.Count)
                        {
                            trim_pts_list.RemoveRange(i, trim_pts_list.Count - i);
                        }
                        break;
                    }
                    else
                    {
                        seek_duration_pts -= diff;
                    }
                }
            }
            if (trim_pts_list.Count == 0)
            {
                throw new Exception();
            }

            // -trimsm
            ulong trim_start_margin_pts = 1 * 90000;
            if (trim_start_margin != "")
            {
                trim_start_margin_pts = (ulong)(TimeSpan.Parse(trim_start_margin).TotalSeconds * 90000) % 0x200000000;
            }

            // -trimem
            ulong trim_end_margin_pts = 1 * 90000;
            if (trim_end_margin != "")
            {
                trim_end_margin_pts = (ulong)(TimeSpan.Parse(trim_end_margin).TotalSeconds * 90000) % 0x200000000;
            }

            // fix trim margin
            for (int i = 0; i < trim_pts_list.Count; i += 2)
            {
                if (trim_start_margin_pts < trim_pts_list[i])
                {
                    trim_pts_list[i] -= trim_start_margin_pts;
                }
                else
                {
                    trim_pts_list[i] = 0;
                }
                trim_pts_list[i + 1] += trim_end_margin_pts;
            }
            ulong[] trim_pts_ary = trim_pts_list.ToArray();

            // detect packet size
            int packet_size = 188;
            {
                byte[] buf = new byte[192 * 4];
                reader.Seek(0, SeekOrigin.Begin);
                if (reader.Read(buf, 0, 192 * 4) != (192 * 4))
                {
                    throw new Exception();
                }
                if ((buf[188 * 0] == 0x47) && (buf[188 * 1] == 0x47) && (buf[188 * 2] == 0x47) && (buf[188 * 3] == 0x47))
                {
                    packet_size = 188;
                }
                else if ((buf[192 * 0 + 4] == 0x47) && (buf[192 * 1 + 4] == 0x47) && (buf[192 * 2 + 4] == 0x47) && (buf[192 * 3 + 4] == 0x47))
                {
                    packet_size = 192;
                }
                else
                {
                    throw new Exception();
                }
            }

            // find first keyframe
            ulong first_keyframe_pts = ulong.MaxValue;
            long first_keyframe_idx = 0;
            reader.Seek(0, SeekOrigin.Begin);
            while (true) {
                byte[] buf = new byte[packet_size];
                if (reader.Read(buf, 0, packet_size) != packet_size)
                {
                    break;
                }
                ulong pts = get_keyframe_pts(buf, packet_size);
                if (pts != ulong.MaxValue)
                {
                    first_keyframe_pts = pts;
                    break;
                }
                first_keyframe_idx++;
            }
            if (first_keyframe_pts == ulong.MaxValue) {
                throw new Exception();
            }

            // find final keyframe
            ulong final_keyframe_pts = ulong.MaxValue;
            long final_keyframe_idx = (reader.Length / packet_size) - 1;
            reader.Seek(packet_size * final_keyframe_idx, SeekOrigin.Begin);
            while (true) {
                byte[] buf = new byte[packet_size];
                if (reader.Read(buf, 0, packet_size) != packet_size)
                {
                    break;
                }
                ulong pts = get_keyframe_pts(buf, packet_size);
                if (pts != ulong.MaxValue)
                {
                    final_keyframe_pts = pts;
                    break;
                }
                reader.Seek(-packet_size * 2, SeekOrigin.Current);
                final_keyframe_idx--;
            }
            if (final_keyframe_pts == ulong.MaxValue) {
                throw new Exception();
            }

            // detect pts wraparound
            bool wraparound_flag = false;
            if (final_keyframe_pts < first_keyframe_pts) {
                final_keyframe_pts += 0x200000000;
                wraparound_flag = true;
                Console.WriteLine("detected pts wraparound");
            }

            // find trim first packet index
            long trim_first_idx = -1;
            long trim_prev_idx = -1;
            {
                ulong trim_first_pts = first_keyframe_pts + trim_pts_ary[0];
                int chunk_scale = 1 << chunk_power; // 2 ^ 10 = 1024
                //int chunk_size = packet_size * chunk_scale; // 188 * 1024, 192 * 1024
                long base_chunk_idx = (long)(((ulong)first_keyframe_idx + (ulong)(final_keyframe_idx - first_keyframe_idx) * trim_pts_ary[0] / (final_keyframe_pts - first_keyframe_pts)) >> chunk_power);
                long base_packet_idx = (long)(base_chunk_idx << chunk_power);
                if (trim_first_pts == first_keyframe_pts)
                {
                    trim_first_idx = first_keyframe_idx;
                    trim_prev_idx = first_keyframe_idx;
                }
                else
                {
                    reader.Seek(packet_size * base_packet_idx, SeekOrigin.Begin);
                    for (long packet_idx = base_packet_idx; packet_idx <= final_keyframe_idx; packet_idx++) {
                        byte[] buf = new byte[packet_size];
                        if (reader.Read(buf, 0, packet_size) != packet_size)
                        {
                            break;
                        }
                        ulong pts = get_keyframe_pts(buf, packet_size);
                        if (pts != ulong.MaxValue)
                        {
                            if (wraparound_flag && (pts < first_keyframe_pts))
                            {
                                pts += 0x200000000;
                            }
                            if (pts >= trim_first_pts)
                            {
                                trim_first_idx = packet_idx;
                                break;
                            }
                            else
                            {
                                trim_prev_idx = packet_idx;
                            }
                        }
                    }
                }
                if (trim_prev_idx < 0)
                {
                    for (long chunk_idx = base_chunk_idx - 1; 0 <= chunk_idx; chunk_idx--) {
                        long seek_packet_idx = chunk_idx * chunk_scale;
                        reader.Seek(packet_size * seek_packet_idx, SeekOrigin.Begin);
                        for (long packet_idx = 0; packet_idx < chunk_scale; packet_idx++) {
                            byte[] buf = new byte[packet_size];
                            if (reader.Read(buf, 0, packet_size) != packet_size)
                            {
                                break;
                            }
                            ulong pts = get_keyframe_pts(buf, packet_size);
                            if (pts != ulong.MaxValue)
                            {
                                if (wraparound_flag && (pts < first_keyframe_pts))
                                {
                                    pts += 0x200000000;
                                }
                                if (pts >= trim_first_pts)
                                {
                                    trim_first_idx = packet_idx + seek_packet_idx;
                                    if (trim_prev_idx >= 0)
                                    {
                                        chunk_idx = 0;
                                    }
                                    break;
                                }
                                else
                                {
                                    trim_prev_idx = packet_idx + seek_packet_idx;
                                    if (trim_first_idx >= 0)
                                    {
                                        chunk_idx = 0;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                if (trim_first_idx < 0) {
                    throw new Exception();
                }
                if (trim_prev_idx < 0) {
                    trim_prev_idx = trim_first_idx;
                }
            }

            bool last_write_status = false;
            bool finished_trim = false;
            int seek_trim_idx = 0;
            List<byte[]> last_gop_packets = new List<byte[]>();

            // trim
            reader.Seek(packet_size * trim_prev_idx, SeekOrigin.Begin);
            while (!finished_trim)
            {
                byte[] buf = new byte[packet_size];
                if (reader.Read(buf, 0, packet_size) != packet_size)
                {
                    break;
                }
                ulong pts = get_keyframe_pts(buf, packet_size);
                bool write_status = false;
                if (pts != ulong.MaxValue)
                {
                    if (wraparound_flag && (pts < first_keyframe_pts))
                    {
                        pts += 0x200000000;
                    }
                    for (int i = seek_trim_idx; i < trim_pts_ary.Length; i += 2)
                    {
                        ulong trim_start_pts = first_keyframe_pts + trim_pts_ary[i];
                        ulong trim_end_pts = first_keyframe_pts + trim_pts_ary[i + 1];
                        if (trim_start_pts <= pts)
                        {
                            if (pts <= trim_end_pts)
                            {
                                if (!last_write_status)
                                {
                                    //dit‚ð‘}“ü‚µ‚½‚Ù‚¤‚ª—Ç‚¢H
                                    foreach (byte[] packet in last_gop_packets)
                                    {
                                        writer.Write(packet, 0, packet.Length);
                                    }
                                }
                                writer.Write(buf, 0, packet_size);
                                write_status = true;
                                seek_trim_idx = i;
                                break;
                            }
                            else if (seek_trim_idx >= (trim_pts_ary.Length - 2))
                            {
                                finished_trim = true;
                                break;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                else
                {
                    if (last_write_status)
                    {
                        writer.Write(buf, 0, packet_size);
                        write_status = true;
                    }
                }
                last_write_status = write_status;
                if (pts != ulong.MaxValue)
                {
                    last_gop_packets.Clear();
                }
                last_gop_packets.Add(buf);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }
        finally
        {
            if (writer != null)
            {
                writer.Close();
            }
            if (reader != null)
            {
                reader.Close();
            }
        }
    }

    static ulong get_keyframe_pts(byte[] buf, int packet_size)
    {
        ulong keyframe_pts = ulong.MaxValue;
        int i = 0;
        bool pts_flag = false;
        ulong pts = ulong.MaxValue;

        if (packet_size == 192)
        {
            i += 4;
        }

        bool sync_byte = (buf[i + 0] == 0x47);
        bool payload_unit_start_indicator = ((buf[i + 1] & 0x40) == 0x40);
        ushort pid = (ushort)((((ushort)(buf[i + 1] & 0x1F)) << 8) + buf[i + 2]);
        bool adaptation_field_indicator = ((buf[i + 3] & 0x20) == 0x20);
        bool payload_indicator = ((buf[i + 3] & 0x10) == 0x10);
        i += 4;

        if (!sync_byte)
        {
            return ulong.MaxValue;
        }

        if (adaptation_field_indicator)
        {
            int adaptation_field_length = buf[i + 0];
            i += 1;

            if (adaptation_field_length > 183)
            {
                return ulong.MaxValue;
            }
            else if (adaptation_field_length > 0)
            {
                i += adaptation_field_length;
            }
        }

        if (payload_unit_start_indicator && payload_indicator && (pid != 0x0000) && (pid != 0x1FFF))
        {
            uint packet_start_code_prefix = (uint)((buf[i + 0] << 8 * 2) + (buf[i + 1] << 8 * 1) + buf[i + 2]);
            i += 3;

            if (packet_start_code_prefix == 0x000001)
            {
                byte stream_id = buf[i + 0];
                i += 3;

                if ((stream_id & 0xF0) == 0xE0) // video
                {
                    i += 1;

                    pts_flag = ((buf[i + 0] & 0x80) == 0x80);
                    i += 1;

                    byte pes_header_data_length = buf[i + 0];
                    i += 1;

                    int j = i;
                    i += pes_header_data_length;

                    if (pts_flag)
                    {
                        pts = (ulong)(((ulong)(buf[j + 0] & 0x0E)) << 8 * 4 - 3) +
                              (ulong)(((ulong)(buf[j + 1]       )) << 8 * 3 - 2) +
                              (ulong)(((ulong)(buf[j + 2] & 0xFE)) << 8 * 2 - 2) +
                              (ulong)(((ulong)(buf[j + 3]       )) << 8 * 1 - 1) +
                              (ulong)(((ulong)(buf[j + 4] & 0xFE)) >>         1);
                        j += 5;
                    }

                    uint mpeg_start_code_prefix;
                    do
                    {
                        mpeg_start_code_prefix = (uint)((buf[i + 0]) << 8 * 2) + (uint)((buf[i + 1]) << 8 * 1) + (uint)buf[i + 2];
                        i += 1;
                    } while ((mpeg_start_code_prefix != 0x000001) && (buf[i - 1] == 0) && (i < 184));
                    i += 2;

                    if (mpeg_start_code_prefix == 0x000001)
                    {
                        byte start_code = buf[i + 0];
                        i += 1;

                        switch (start_code)
                        {
                            case 0x00: // H.262 picture_header
                                byte picture_coding_type = (byte)((buf[i + 1] & 0x38) >> 3);
                                i += 3;
                                switch (picture_coding_type)
                                {
                                    case 1: // I
                                        break;
                                    case 2: // P
                                        break;
                                    case 3: // B
                                        break;
                                    case 4: // D
                                        break;
                                }
                                break;
                            case 0xB3: // H.262 sequence_header
                                if (pts_flag)
                                {
                                    keyframe_pts = pts;
                                }
                                break;
                            case 0xB8: // H.262 group_header
                                break;
                            default:
                                if ((start_code & 0x80) == 0x00)
                                {
                                    byte nal_unit_type = (byte)(start_code & 0x1F);
                                    switch (nal_unit_type)
                                    {
                                        case 1: // H.264 non-IDR picture
                                            break;
                                        case 2: // H.264 slice data partition A
                                            break;
                                        case 3: // H.264 slice data partition B
                                            break;
                                        case 4: // H.264 slice data partition C
                                            break;
                                        case 5: // H.264 IDR picture
                                            break;
                                        case 6: // H.264 SEI
                                            break;
                                        case 7: // H.264 Sequence parameter set
                                            break;
                                        case 8: // H.264 Picture parameter set
                                            break;
                                        case 9: // H.264 Access unit delimiter
                                            if (pts_flag)
                                            {
                                                keyframe_pts = pts;
                                            }
                                            break;
                                        case 10: // H.264 End of sequence
                                            break;
                                        case 11: // H.264 End of stream
                                            break;
                                        case 12: // H.264 Filler data
                                            break;
                                    }
                                }
                                break;
                        }
                    }
                }
                else if ((stream_id & 0xE0) == 0xC0) // audio
                {
                }
            }
        }

        return keyframe_pts;
    }
}


