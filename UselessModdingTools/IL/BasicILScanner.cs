using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace UselessModdingTools.IL;

internal readonly record struct Instruction(OpCode OpCode, byte[] OperandIL, int Position);

internal static class BasicILScanner
{
    public static Dictionary<OpCode, OpCode> StoreLoadReciprocals { get; set; }

    private static readonly Dictionary<short, OpCode> ops = [];

    static BasicILScanner()
    {
        foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            ops[((OpCode)f.GetValue(null)!).Value] = (OpCode)f.GetValue(null)!;
        }

        StoreLoadReciprocals = [];
        StoreLoadReciprocals[OpCodes.Stfld] = OpCodes.Ldfld;
        StoreLoadReciprocals[OpCodes.Stloc] = OpCodes.Ldloc;
        StoreLoadReciprocals[OpCodes.Stloc_0] = OpCodes.Ldloc_0;
        StoreLoadReciprocals[OpCodes.Stloc_1] = OpCodes.Ldloc_1;
        StoreLoadReciprocals[OpCodes.Stloc_2] = OpCodes.Ldloc_2;
        StoreLoadReciprocals[OpCodes.Stloc_3] = OpCodes.Ldloc_3;
        StoreLoadReciprocals[OpCodes.Stloc_S] = OpCodes.Ldloc_S;
        foreach (var kvp in StoreLoadReciprocals.ToArray())
        {
            StoreLoadReciprocals[kvp.Value] = kvp.Key;
        }
    }

    public static bool TryReadIL(MethodInfo method, [NotNullWhen(true)] out List<Instruction>? instructions, [NotNullWhen(true)] out HashSet<int>? branchTargets)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();

        if (il == null)
        {
            instructions = null;
            branchTargets = null;
            return false;
        }

        instructions = [];
        branchTargets = [];
        int size;

        for (int pos = 0; pos < il.Length; pos += size)
        {
            int p = pos;

            ushort code = il[pos++];
            if (code == 0xFE)
                code = (ushort)(0xFE00 + il[pos++]);

            OpCode op = ops[(short)code];
            size = OperandSize(op);

            if (size == -1)
            {
                size = 4 + (BitConverter.ToInt32(il, pos) * 4); // variable size switch op
            }

            byte[] operand = il[pos..(pos + size)];
            instructions.Add(new(op, operand, p));

            switch (op.OperandType)
            {
                case OperandType.ShortInlineBrTarget:
                    {
                        int target = pos + 1 + (sbyte)il[pos]; // 1-byte signed offset, relative to NEXT instruction
                        branchTargets.Add(target);
                        break;
                    }
                case OperandType.InlineBrTarget:
                    {
                        int offset = BitConverter.ToInt32(il, pos); // 4-byte signed offset, relative to NEXT instruction
                        int target = pos + 4 + offset;
                        branchTargets.Add(target);
                        break;
                    }
                case OperandType.InlineSwitch:
                    {
                        int count = BitConverter.ToInt32(il, pos);
                        int baseAddr = pos + 4 + (count * 4); // end of switch operand
                        for (int i = 0; i < count; i++)
                        {
                            int offset = BitConverter.ToInt32(il, pos + 4 + i * 4);
                            branchTargets.Add(baseAddr + offset);
                        }
                        break;
                    }
            }
        }

        return true;
    }

    private static int OperandSize(OpCode op) => op.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget => 1,
        OperandType.ShortInlineI => 1,
        OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 => 8,
        OperandType.InlineR => 8,
        OperandType.InlineSwitch => -1, // special: 4 + 4*n
        _ => 4 // InlineMethod, InlineString, InlineI, InlineField, etc.
    };

    public static bool IsLoadNumericConstant(OpCode op) =>
        op == OpCodes.Ldc_I4_M1
        || op == OpCodes.Ldc_I4_0
        || op == OpCodes.Ldc_I4_1
        || op == OpCodes.Ldc_I4_2
        || op == OpCodes.Ldc_I4_3
        || op == OpCodes.Ldc_I4_4
        || op == OpCodes.Ldc_I4_5
        || op == OpCodes.Ldc_I4_6
        || op == OpCodes.Ldc_I4_7
        || op == OpCodes.Ldc_I4_8
        || op == OpCodes.Ldc_I4_S
        || op == OpCodes.Ldc_I4
        || op == OpCodes.Ldc_I8
        || op == OpCodes.Ldc_R4
        || op == OpCodes.Ldc_R8;

    public static bool IsArithmatic(OpCode op) =>
       op == OpCodes.Add
       || op == OpCodes.Sub
       || op == OpCodes.Mul
       || op == OpCodes.Div;

    public static bool IsLoad(OpCode op) =>
        op == OpCodes.Ldfld
        || op == OpCodes.Ldloc
        || op == OpCodes.Ldloc_0
        || op == OpCodes.Ldloc_1
        || op == OpCodes.Ldloc_2
        || op == OpCodes.Ldloc_3
        || op == OpCodes.Ldloc_S;

    public static bool IsStore(OpCode op) =>
        op == OpCodes.Stfld
        || op == OpCodes.Stloc
        || op == OpCodes.Stloc_0
        || op == OpCodes.Stloc_1
        || op == OpCodes.Stloc_2
        || op == OpCodes.Stloc_3
        || op == OpCodes.Stloc_S;

    public static int NetStackDepth(OpCode opCode, MethodBase? called)
    {
        if (called != null && (opCode == OpCodes.Call || opCode == OpCodes.Callvirt || opCode == OpCodes.Newobj)) // box and unbox too?
        {
            ParameterInfo[] parms = called.GetParameters();

            int paramCount = parms.Length + (called.IsStatic ? 0 : 1);
            int returns = (called is MethodInfo m && m.ReturnType != typeof(void)) ? 1 : 0;

            if (opCode == OpCodes.Newobj)
            {
                paramCount -= 1; // no 'this' for ctor
                returns = 1; // pushes the new instance
            }

            int depth = returns - paramCount;
            return depth;
        }
        else
        {
            return opCode.EvaluationStackDelta;
        }
    }

    public static int GetIntFromIL(OpCode opCode, byte[] operandIL)
    {
        return opCode.Value switch
        {
            0x15 => -1, // ldc.i4.m1
            0x16 => 0,  // ldc.i4.0
            0x17 => 1,  // ldc.i4.1
            0x18 => 2,  // ldc.i4.2
            0x19 => 3,  // ldc.i4.3
            0x1A => 4,  // ldc.i4.4
            0x1B => 5,  // ldc.i4.5
            0x1C => 6,  // ldc.i4.6
            0x1D => 7,  // ldc.i4.7
            0x1E => 8,  // ldc.i4.8
            0x1F => (int)(sbyte)operandIL[0], // ldc.i4.s — 1 signed byte
            0x20 => BitConverter.ToInt32(operandIL, 0), // ldc.i4 — 4 bytes, little-endian
            0x21 => (int)BitConverter.ToInt64(operandIL, 0), // or keep as long  // ldc.i8 — 8 bytes, little-endian
            0X22 => (int)BitConverter.ToSingle(operandIL, 0), // ldc.r4 (float)
            0X23 => (int)BitConverter.ToDouble(operandIL, 0), // ldc.r8 (double)
            _ => -1,
        };
    }

    public static string GetStringFromIL(Module module, byte[] operandIL)
    {
        int token = BitConverter.ToInt32(operandIL, 0);
        string s = module.ResolveString(token);
        return s;
    }
}
