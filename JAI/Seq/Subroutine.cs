﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Be.IO;
using System.IO;

namespace JaiSeqX.JAI.Seq
{

   
    
    public class Subroutine
    {
       
        byte[] SeqData; // Full .BMS  file.  
        BeBinaryReader Sequence; // Reader for SeqData
        private int baseAddress; // The address at which this subroutine starts in the file.
        public byte last_opcode; // The last opcode that was executed. 

        public int[] Registers;  // JAISeq Registers.
        public int[] Ports;  //  Ports, for ReadPort and WritePort -- used for interfacing with external data or game events.
        public Stack<uint> AddrStack; // JAISeq return stack, depth of 8, used for CALL and RETURN commands.

        public Queue<JAISeqExecutionFrame> history; // Execution history. 
 
        public int[] rI; // Internal Integer registers  -- for interfacing with sequence. 
        public float[] rF; // Internal Float registers -- for interfacing with sequence.

        public int pc
        {
            get
            {
                return (int)Sequence.BaseStream.Position;
            }
        }
        public int pcl;

        public Subroutine(ref byte[] BMSData,int BaseAddr)
        {
            SeqData = BMSData; // 
            AddrStack = new Stack<uint>(8); // JaiSeq has a stack depth of 8
            history = new Queue<JAISeqExecutionFrame>(16); // Ill keep an opcode depth of 16      
            Sequence = new BeBinaryReader(new MemoryStream(BMSData)); // Make a reader for this. 
            Sequence.BaseStream.Position = BaseAddr; // Set its position to the base address. 
            baseAddress = BaseAddr; // store the base address
        }


        private void skip(int bytes)
        {
            Sequence.BaseStream.Seek(bytes, SeekOrigin.Current);
        }

        private void reset()
        {
            Sequence.BaseStream.Position = baseAddress;
        }

        public void jump(int pos)
        {
            Sequence.BaseStream.Position = pos;
        }

        public JAISeqEvent loadNextOp()
        {
            if (history.Count == 16)  // Opstack is full
                history.Dequeue(); // push the one off the end. 
            var historyPos = (int)Sequence.BaseStream.Position; // store push address for FIFO stack. 
            pcl = (int)Sequence.BaseStream.Position; // Store the last known program counter.  
            byte current_opcode = Sequence.ReadByte(); // Reads the current byte in front of the cursor. 
            last_opcode = current_opcode;
            history.Enqueue(new JAISeqExecutionFrame { opcode = last_opcode,  address = historyPos} ); // push opcode to FIFO stack

            if (current_opcode < 0x80)  // anything 0x80  or under is a NOTE_ON, this lines up with MIDI notes.
            {
                State.note = current_opcode; // The note on event is laid out like a piano with 127 (0x7F1) keys. 
                // So this means that the first 0x80 bytes are just pressing the individual keys.
                State.voice = Sequence.ReadByte(); // The next byte tells the voice, 0-8
                State.vel = Sequence.ReadByte(); // And finally, the next byte will tell the velocity 
                return JaiEventType.NOTE_ON; // Return the note on event. 

            } else if (current_opcode==(byte)JaiSeqEvent.WAIT_8) // Contrast to above, the opcode between these two is WAIT_U8
            {
                State.delay += Sequence.ReadByte(); // Add u8 ticks to the delay.  

                return JaiEventType.DELAY;
            } else if (current_opcode < 0x88) // We already check if it's 0x80, so anything between here will be 0x81 and 0x87
            {
                // Only the first 7 bits are going to determine which voice we're stopping. 
                State.voice = (byte)(current_opcode & 0x7F);
                return JaiEventType.NOTE_OFF;
            } else // Finally, we can fall into our CASE statement. 
            {
                switch (current_opcode)
                {
                    /* Delays and waits */
                    case (byte)JaiSeqEvent.WAIT_16: // Wait (UInt16)
                        State.delay = Sequence.ReadUInt16(); // Add to the state delay
                      
                        return JaiEventType.DELAY;

                    case (byte)JaiSeqEvent.WAIT_VAR: // Wait (VLQ) see readVlq function. 
                        State.delay += Helpers.ReadVLQ(Sequence);
                        return JaiEventType.DELAY;

                    /* Logical jumps */ 

                    case (byte)JaiSeqEvent.JUMP: // Unconditional jump
                        State.jump_mode = 0; // Set jump mode to 0
                        State.jump_address = Sequence.ReadInt32(); // Absolute address. 
                        return JaiEventType.JUMP;

                    case (byte)JaiSeqEvent.JUMP_COND: // Jump based on mode
                        State.jump_mode = Sequence.ReadByte();
                        State.jump_address = (int)Helpers.ReadUInt24BE(Sequence); // pointer
                        return JaiEventType.JUMP;

                    case (byte)JaiSeqEvent.RET_COND:
                        State.jump_mode = Sequence.ReadByte();
                        
                        return JaiEventType.RET;
                    case (byte)JaiSeqEvent.CALL_COND:
                        State.jump_mode = Sequence.ReadByte();
                        State.jump_address = (int)Helpers.ReadUInt24BE(Sequence);
                        return JaiEventType.CALL;
                    case (byte)JaiSeqEvent.RET:
                        return JaiEventType.RET;
                   


                    /* Tempo Control */

                    case (byte)JaiSeqEvent.J2_SET_ARTIC: // The very same.
                        {
                            var type = Sequence.ReadByte();
                            var val = Sequence.ReadInt16();
                            if (type == 0x62)
                            {
                                State.ppqn = val;
                            }
                            return JaiEventType.TIME_BASE;
                        }
                    case (byte)JaiSeqEvent.TIME_BASE: // Set ticks per quarter note.
                        State.ppqn =  (short)(Sequence.ReadInt16()  );
                        //State.bpm = 100;
                        Console.WriteLine("Timebase ppqn set {0}", State.ppqn);
                        return JaiEventType.TIME_BASE;

                    case (byte)JaiSeqEvent.J2_TEMPO: // Set BPM, Same format
                    case (byte)JaiSeqEvent.TEMPO: // Set BPM
                        State.bpm = (short)(Sequence.ReadInt16() );
                        return JaiEventType.TIME_BASE;

                    /* Track Control */

                    case (byte)JaiSeqEvent.OPEN_TRACK:
                        State.track_id = Sequence.ReadByte();
                        State.track_address = (int)Helpers.ReadUInt24BE(Sequence);  // Pointer to track inside of BMS file (Absolute) 
                        return JaiEventType.NEW_TRACK;
                    case (byte)JaiSeqEvent.FIN:
                        return JaiEventType.HALT;

                    case (byte)JaiSeqEvent.J2_SET_BANK:
                        State.voice_bank = Sequence.ReadByte();
                        return JaiEventType.BANK_CHANGE;

                    case (byte)JaiSeqEvent.J2_SET_PROG:
                        State.voice_program = Sequence.ReadByte();
                        return JaiEventType.PROG_CHANGE;

                    /* Parameter control */


                    case (byte)JaiSeqEvent.J2_SET_PERF_8:
                        State.param = Sequence.ReadByte();
                        State.param_value = Sequence.ReadByte();
                        return JaiEventType.PARAM;

                    case (byte)JaiSeqEvent.J2_SET_PERF_16:
                        State.param = Sequence.ReadByte();
                        State.param_value = Sequence.ReadInt16();
                        return JaiEventType.PARAM;

                    case (byte)JaiSeqEvent.PARAM_SET_8: // Set track parameters (Usually used for instruments)
                        State.param = Sequence.ReadByte();
                        State.param_value = Sequence.ReadByte(); 
                        if (State.param==0x20) // 0x20 is bank change 
                        {
                            State.voice_bank = (byte)State.param_value; 
                            return JaiEventType.BANK_CHANGE;
                        }
                        if (State.param == 0x21) // 0x21 is program change 
                        {
                            State.voice_program = (byte)State.param_value;
                            return JaiEventType.PROG_CHANGE;
                        }
                        return JaiEventType.PARAM;

                    case (byte)JaiSeqEvent.PARAM_SET_16: // Set track parameters (Usually used for instruments)
                        State.param = Sequence.ReadByte();
                        State.param_value = Sequence.ReadInt16();
                        if (State.param == 0x20) // 0x20 is bank change 
                        {
                            State.voice_bank = (byte)State.param_value;
                            return JaiEventType.BANK_CHANGE;
                        }
                        if (State.param == 0x21) // 0x21 is program change 
                        {
                            State.voice_program = (byte)State.param_value;
                            return JaiEventType.PROG_CHANGE;
                        }
                        return JaiEventType.PARAM;
                    case (byte)JaiSeqEvent.PRINTF:
                        var lastread = -1;
                        string v = "";
                        while (lastread!=0)
                        {
                            lastread = Sequence.ReadByte();
                            v += (char)lastread;
                        }
                       // Sequence.ReadByte();
                        Console.WriteLine(v);

                        return JaiEventType.UNKNOWN;

                    /* PERF Control*/
                    /* Perf structure is as follows
                     * <byte> type 
                     * <?> val
                     * (<?> dur)
                    */

                    case (byte)JaiSeqEvent.PERF_U8_NODUR:
                        State.perf = Sequence.ReadByte();
                        State.perf_value = Sequence.ReadByte();
                        
                        State.perf_duration = 0;
                        State.perf_type = 1;
                        State.perf_decimal = ((double)State.perf_value / 0xFF);
                        return JaiEventType.PERF;

                    case (byte)JaiSeqEvent.PERF_U8_DUR_U8:
                        State.perf = Sequence.ReadByte();
                        State.perf_value = Sequence.ReadByte();
                        State.perf_duration = Sequence.ReadByte();
                        State.perf_type = 1;
                        State.perf_decimal = ((double)State.perf_value / 0xFF);
                        return JaiEventType.PERF;

                    case (byte)JaiSeqEvent.PERF_U8_DUR_U16:

                        State.perf = Sequence.ReadByte();
                        State.perf_value = Sequence.ReadByte();
                        State.perf_duration = Sequence.ReadUInt16();
                        State.perf_type = 1;
                        State.perf_decimal = ((double)State.perf_value / 0xFF);
                        return JaiEventType.PERF;

                    case (byte)JaiSeqEvent.PERF_S8_NODUR:
                        {
                            State.perf = Sequence.ReadByte();
                            var b = Sequence.ReadByte(); // Lazy byte signage, apparently C#'s SByte is broken.
                            State.perf_value = (b > 0x7F) ? b - 0xFF : b;
                            State.perf_duration = 0;
                            State.perf_type = 2;
                            State.perf_decimal = ((double)(State.perf_value) / 0x7F);
                            return JaiEventType.PERF;
                        }
                    case (byte)JaiSeqEvent.PERF_S8_DUR_U8:
                        {
                            State.perf = Sequence.ReadByte();
                            var b = Sequence.ReadByte(); // Lazy byte signage, apparently C#'s SByte is broken.
                            State.perf_value = (b > 0x7F) ? b - 0xFF : b;
                            State.perf_duration = Sequence.ReadByte();
                            State.perf_type = 2;
                            State.perf_decimal = ((double)(State.perf_value) / 0x7F);
                            return JaiEventType.PERF;
                        }

                    case (byte)JaiSeqEvent.PERF_S8_DUR_U16:
                        {
                            State.perf = Sequence.ReadByte();
                            var b = Sequence.ReadByte(); // Lazy byte signage, apparently C#'s SByte is broken.
                            State.perf_value = State.perf_value = (b > 0x7F) ? b - 0xFF : b;
                            State.perf_duration = Sequence.ReadUInt16();
                            State.perf_type = 2;
                            State.perf_decimal = ((double)(State.perf_value) / 0x7F);
                            return JaiEventType.PERF;
                        }

                    case (byte)JaiSeqEvent.PERF_S16_NODUR:
                        State.perf = Sequence.ReadByte();
                        State.perf_value = Sequence.ReadInt16();
                        State.perf_duration = 0;
                        State.perf_type = 3;
                        State.perf_decimal = ((double)(State.perf_value) / 0x7FFF);
                        return JaiEventType.PERF;

                    case (byte)JaiSeqEvent.PERF_S16_DUR_U8:
                        State.perf = Sequence.ReadByte();
                        State.perf_value = Sequence.ReadInt16();
                        State.perf_duration = Sequence.ReadByte();
                        State.perf_type = 3;
                        State.perf_decimal = ((double)(State.perf_value) / 0x7FFF);
                        return JaiEventType.PERF;

                    case (byte)JaiSeqEvent.PERF_S16_DUR_U16:
                        State.perf = Sequence.ReadByte();
                        State.perf_value = Sequence.ReadInt16();
                        State.perf_duration = Sequence.ReadUInt16();
                        State.perf_type = 3;
                        State.perf_decimal = ((double)(State.perf_value) / 0x7FFF);
                        return JaiEventType.PERF;


                    /* J2 Opcodes */


                    /* Unsure as of yet, but we have to keep alignment */
                    case 0xE7:
                       skip(2);
                       // Console.WriteLine(Sequence.ReadByte());
                        //Console.WriteLine(Sequence.ReadByte());

                        return JaiEventType.DEBUG;
                    case 0xDD:
                    case 0xED:
                        skip(3);
                        return JaiEventType.UNKNOWN;
                    case 0xEF:
                    case 0xF9:
                    case 0xE6:
                  
                        skip(2);
                        return JaiEventType.UNKNOWN;
                    case 0xA0:
                    case (byte)JaiSeqEvent.ADDR: // 
                        skip(2);
                        return JaiEventType.UNKNOWN;
                    case 0xA3:
                        skip(2);
                        return JaiEventType.UNKNOWN;
                    case 0xA5:
                        skip(2);
                        return JaiEventType.UNKNOWN;
                    case 0xA7:
                        skip(2);
                        return JaiEventType.UNKNOWN;
                    case 0xA9:
                        skip(4);
                        return JaiEventType.UNKNOWN;
                    case 0xAA:
                        skip(4);
                        return JaiEventType.UNKNOWN;
                    case 0xAD:
                       // State.delay += 0xFFFF;
                       // Add (byte) register.  + (short) value
                       // 
                        skip(3);
                        return JaiEventType.UNKNOWN;
                    case 0xAE:                        
                        return JaiEventType.UNKNOWN;
                    case 0xB1:
                    case 0xB2:
                    case 0xB3:
                    case 0xB4:
                    case 0xB5:
                    case 0xB6:
                    case 0xB7:
                    int flag = Sequence.ReadByte();
                        if (flag == 0x40) { skip(2); }
                        if (flag == 0x80) { skip(4); }
                        return JaiEventType.UNKNOWN;
                    case 0xDB:
                   
                    case 0xDF:
                    
                        skip(4);
                        return JaiEventType.UNKNOWN;
                    case 0xCB:
                    case 0xBE:
                        skip(2);
                        return JaiEventType.UNKNOWN;
                    case 0xCC:
                        skip(2);
                        return JaiEventType.UNKNOWN;
                    case 0xCF:
                        skip(1);
                        return JaiEventType.UNKNOWN;
                    case 0xD0:
                    case 0xD1:
                    case 0xD2:
                    case 0xD5:
                    case 0xD9:

                    case 0xDE:
                    case 0xDA:
                   
                        skip(1);
                        return JaiEventType.UNKNOWN;
                    case 0xF1:
                    case 0xF4:

                    case 0xD6:
                        skip(1);
                        //Console.WriteLine(Sequence.ReadByte());
                        return JaiEventType.DEBUG;
                    case 0xBC:
                        return JaiEventType.UNKNOWN;
                }
            }
            return JaiEventType.UNKNOWN_ALIGN_FAIL;
        }



    }
}
