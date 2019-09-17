﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace JaiSeqX.JAI.Seq
{
    public partial class JAISeqSubroutine
    {
        public JAISeqEvent ProcessParamOps(byte currnet_opcode)
        {
            switch (currnet_opcode)
            {
                /* PORT COMANDS */
                case (byte)JAISeqEvent.READPORT:
                    {
                        var port_id = Sequence.ReadByte(); // Read Port ID
                        var dest_reg = Sequence.ReadByte(); //  Read Destination Register
                        rI[0] = port_id; // push id to ir0
                        rI[1] = dest_reg; // push register to ir1
                        Registers[dest_reg] = Ports[port_id]; // Do move operation
                        return JAISeqEvent.READPORT;
                    }
                /* Tempo Control */
                case (byte)JAISeqEvent.J2_SET_ARTIC: // The very same.
                    {
                        var type = Sequence.ReadByte();
                        var val = Sequence.ReadInt16();
                        rI[0] = type; // 0x62 is  tempo, tho. 
                        rI[1] = val;
                        return JAISeqEvent.J2_SET_ARTIC;
                    }
                case (byte)JAISeqEvent.TIME_BASE: // Set ticks per quarter note.
                    {
                        rI[0] = (short)(Sequence.ReadInt16());
                        Console.WriteLine("Timebase ppqn set {0}", rI[0]);
                        return JAISeqEvent.TIME_BASE;
                    }
                case (byte)JAISeqEvent.J2_TEMPO: // Set BPM, Same format
                case (byte)JAISeqEvent.TEMPO: // Set BPM
                    {
                        rI[0] = (short)(Sequence.ReadInt16());
                        return JAISeqEvent.TIME_BASE;
                    }
                /* Parameter control */
                case (byte)JAISeqEvent.J2_SET_PARAM_8:
                    {
                        rI[0] = Sequence.ReadByte();
                        rI[1] = Sequence.ReadByte();
                        Registers[(byte)rI[0]] = (short)rI[1];
                        return JAISeqEvent.J2_SET_PARAM_8;
                    }
                case (byte)JAISeqEvent.J2_SET_PARAM_16:
                    {
                        rI[0] = Sequence.ReadByte();
                        rI[1] = Sequence.ReadInt16();
                        Registers[(byte)rI[0]] = (short)rI[1];
                        return JAISeqEvent.J2_SET_PARAM_16;
                    }
                case (byte)JAISeqEvent.PARAM_SET_R:
                    {
                        var register1 = Sequence.ReadByte();
                        var register2 = Sequence.ReadByte();
                        rI[0] = register1;
                        rI[2] = register2;
                        Registers[register1] = Registers[register2];
                        return JAISeqEvent.PARAM_SET_R;
                    }
                case (byte)JAISeqEvent.PARAM_SET_8: // Set track parameters (Usually used for instruments)
                    {
                        var reg = Sequence.ReadByte();
                        var val = Sequence.ReadByte();
                        rI[0] = reg;
                        rI[1] = val;
                        Registers[reg] = val;
                        return JAISeqEvent.PARAM_SET_8;
                    }
                case (byte)JAISeqEvent.PARAM_SET_16: // Set track parameters (Usually used for instruments)
                    {
                        var reg = Sequence.ReadByte();
                        var val = Sequence.ReadInt16();
                        rI[0] = reg;
                        rI[1] = val;
                        Registers[reg] = val;
                        return JAISeqEvent.PARAM_SET_16;
                    }
                case (byte)JAISeqEvent.J2_SET_BANK:
                    {
                        rI[0] = Sequence.ReadByte();
                        return JAISeqEvent.J2_SET_BANK;
                    }
                case (byte)JAISeqEvent.J2_SET_PROG:
                    {
                        rI[0] = Sequence.ReadByte();
                        return JAISeqEvent.J2_SET_PROG;
                    }

            }
            return JAISeqEvent.UNKNOWN;
        }
    }
}