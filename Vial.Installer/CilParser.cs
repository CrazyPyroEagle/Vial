using dnlib.DotNet;
using dnlib.DotNet.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Vial.Installer
{
    sealed class CilParser
    {
        private static Dictionary<Code, OpCode> opCodes;

        static CilParser()
        {
            opCodes = new Dictionary<Code, OpCode>();
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (typeof(OpCode).IsAssignableFrom(field.FieldType))
                {
                    OpCode code = (OpCode)field.GetValue(null);
                    opCodes.Add(code.Code, code);
                }
            }
        }
        
        private readonly Module resolver;
        private readonly IList<Local> locals;
        private readonly byte[] il;
        private int index;

        public CilParser(Module resolver, IList<Local> locals, byte[] il)
        {
            this.resolver = resolver;
            this.locals = locals;
            this.il = il;
        }

        public List<Instruction> Parse()
        {
            List<Instruction> instructions = new List<Instruction>();
            while (index < il.Length) instructions.Add(ParseInstruction());
            foreach (Instruction inst in instructions)
            {
                switch (inst.OpCode.OperandType)
                {
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.InlineBrTarget:
                        inst.Operand = instructions.AtOffset((int)inst.Operand);
                        break;
                    case OperandType.InlineSwitch:
                        inst.Operand = ((int[])inst.Operand).Select(offset => instructions.AtOffset(offset)).ToArray();
                        break;
                }
            }
            return instructions;
        }

        private Instruction ParseInstruction()
        {
            Instruction result = new Instruction
            {
                Offset = (uint)index,
                OpCode = ParseOpCode()
            };
            switch (result.OpCode.OperandType)
            {
                case OperandType.ShortInlineBrTarget:
                    result.Operand = il[index++] + index;
                    break;
                case OperandType.ShortInlineI:
                    if (result.OpCode.Code == Code.Ldc_I4_S) result.Operand = (sbyte)il[index++];
                    else result.Operand = il[index++];
                    break;
                case OperandType.ShortInlineVar:
                    result.Operand = locals[il[index++]];
                    break;
                case OperandType.InlineVar:
                    result.Operand = locals[Int16()];
                    break;
                case OperandType.InlineBrTarget:
                    result.Operand = Int32() + index;
                    break;
                case OperandType.InlineI:
                    result.Operand = Int32();
                    break;
                case OperandType.ShortInlineR:
                    result.Operand = BitConverter.ToSingle(BitConverter.GetBytes(Int32()), 0);
                    break;
                case OperandType.InlineField:
                    result.Operand = resolver.ResolveField(Int32());
                    break;
                case OperandType.InlineMethod:
                    result.Operand = resolver.ResolveMethod(Int32());
                    break;
                case OperandType.InlineSig:
                    result.Operand = resolver.ResolveSignature(Int32());
                    break;
                case OperandType.InlineTok:
                    result.Operand = resolver.ResolveMember(Int32());
                    break;
                case OperandType.InlineType:
                    result.Operand = resolver.ResolveType(Int32());
                    break;
                case OperandType.InlineString:
                    result.Operand = resolver.ResolveString(Int32());
                    break;
                case OperandType.InlineI8:
                    result.Operand = Int64();
                    break;
                case OperandType.InlineR:
                    result.Operand = BitConverter.Int64BitsToDouble(Int64());
                    break;
                case OperandType.InlineSwitch:
                    int[] relOffsets = new int[Int32()];
                    int offset = relOffsets.Length * 4;
                    for (int index = 0; index < relOffsets.Length; index++) relOffsets[index] = Int32() + offset;
                    result.Operand = relOffsets;
                    break;
                case OperandType.InlinePhi:
                case OperandType.NOT_USED_8:
                    throw new ArgumentException("operand length unknown");
            }
            return result;
        }

        private OpCode ParseOpCode()
        {
            Code code = (Code)il[index++];
            if (opCodes.TryGetValue(code, out OpCode opCode)) return opCode;
            code = (Code)(il[index++] << 8) | code;
            if (opCodes.TryGetValue(code, out opCode)) return opCode;
            throw new ArgumentException("unknown opcode " + code);
        }
        
        private int Int16() => il[index++] | il[index++] << 8;
        private int Int32() => il[index++] | il[index++] << 8 | il[index++] << 16 | il[index++] << 24;
        private long Int64() => il[index++] | (long)il[index++] << 8 | (long)il[index++] << 16 | (long)il[index++] << 24 | (long)il[index++] << 32 | (long)il[index++] << 40 | (long)il[index++] << 48 | (long)il[index++] << 56;
    }
}
