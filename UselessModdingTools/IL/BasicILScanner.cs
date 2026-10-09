using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;

namespace UselessModdingTools.IL;

public readonly record struct Instruction(OpCode OpCode, byte[] OperandIL, string? Text, int Position, int Depth, Branch? Branch, bool IsExceptionHandlerLead);

public readonly record struct Branch(int Position, int Target, int[]? SwitchTargets);
//public readonly record struct Switch(object? Operand, int[] Targets);

public record class TargetInfo(int Count, int IncomingDepth) { public int Count { get; set; } = Count; public int IncomingDepth { get; set; } = IncomingDepth; }

public static class BasicILScanner
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

    public static bool TryReadIL(MethodInfo baseMethod, [NotNullWhen(true)] out List<Instruction>? instructions, [NotNullWhen(true)] out Dictionary<int, TargetInfo>? branchTargets)
    {
        MethodBody? methodBody = baseMethod.GetMethodBody();
        byte[]? il = methodBody?.GetILAsByteArray();

        if (methodBody == null || il == null)
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
            int depth = 0;
            string? text = null;
            bool exeption = false;
            if (instructions.Count > 0)
            {
                OpCode opCode = instructions[^1].OpCode;
                if (IsUnconditionalBranch(opCode))
                {
                    if (opCode == OpCodes.Leave || opCode == OpCodes.Leave_S) // Exception handling
                    {
                        foreach (ExceptionHandlingClause clause in methodBody.ExceptionHandlingClauses)
                        {
                            if (clause.HandlerOffset == p)
                            {
                                exeption = true;

                                if (clause.Flags == ExceptionHandlingClauseOptions.Clause || clause.Flags == ExceptionHandlingClauseOptions.Filter)
                                {
                                    // Compiler clears the stack, then pushes the exeption onto the stack externally
                                    depth = 1;
                                    text += $" [{clause.CatchType?.ToString()}]" ?? " [Exception]";
                                }
                                else
                                {
                                    depth = 0;
                                }
                            }
                        }
                    }
                    if (!exeption && branchTargets.TryGetValue(p, out TargetInfo? info))
                    {
                        depth = info.IncomingDepth;
                    }
                }
                else
                {
                    depth = instructions[^1].Depth;
                }
            }
            depth += NetStackDepth(op, baseMethod, GetCalledMethod(baseMethod, op, operand));

            Branch? branch = default;
            if (IsBranch(op))
            {
                int targetPos = GetRawTargetOfBranch(op, il, pos, out int[]? switchTargets);

                if (switchTargets == null)
                {
                    AddTarget(branchTargets, targetPos, depth);
                }
                else
                {
                    foreach (int tar in switchTargets)
                    {
                        AddTarget(branchTargets, tar, depth);
                    }
                }

                branch = new(pos, targetPos, switchTargets);
            }

            static void AddTarget(Dictionary<int, TargetInfo> branchTargets, int targetPos, int depth)
            {
                if (!branchTargets.TryGetValue(targetPos, out TargetInfo? info)) // Target depth takes the max of all incoming branch depths (but i think they should all be the same?)
                {
                    info = new(1, depth);
                    branchTargets[targetPos] = info;
                }
                else
                {
                    info.Count++;
                    if (depth > info.IncomingDepth)
                    {
                        info.IncomingDepth = depth;
                    }
                }
            }

            instructions.Add(new(op, operand, text, p, depth, branch, exeption));
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

    public static bool IsCall(OpCode op) =>
        op == OpCodes.Call
        || op == OpCodes.Callvirt
        || op == OpCodes.Newobj;

    public static bool IsBranch(OpCode op) =>
        op.OperandType == OperandType.InlineBrTarget
        || op.OperandType == OperandType.ShortInlineBrTarget
        || op.OperandType == OperandType.InlineSwitch;

    public static bool IsUnconditionalBranch(OpCode op) =>
        op == OpCodes.Br
        || op == OpCodes.Br_S
        || op.OperandType == OperandType.InlineSwitch
        || op == OpCodes.Leave
        || op == OpCodes.Leave_S;
    // Also leave/leave.s, but these are specialized

    public static object? GetOperand(MethodInfo baseMethod, Instruction instruction)
    {
        if (IsBranch(instruction.OpCode) && instruction.Branch != null)
        {
            //if (instruction.OpCode == OpCodes.Switch && instruction.Branch?.SwitchTargets != null)
            //    return new Switch(GetOperand(baseMethod, instruction.OpCode, instruction.OperandIL), instruction.Branch.Value.SwitchTargets);

            return instruction.Branch;
        }

        return GetOperand(baseMethod, instruction.OpCode, instruction.OperandIL);
    }

    public static object? GetOperand(MethodInfo baseMethod, OpCode op, byte[] il)
    {
        if (IsLoadNumericConstant(op))
        {
            return GetIntFromIL(op, il);
        }
        else if (op == OpCodes.Ldstr)
        {
            return GetStringFromIL(baseMethod.Module, il);
        }
        else if (op == OpCodes.Ldfld || op == OpCodes.Stfld)
        {
            int token = BitConverter.ToInt32(il, 0);
            return baseMethod.Module.ResolveField(token, baseMethod.DeclaringType?.GetGenericArguments(), baseMethod.GetGenericArguments());
        }
        else if (IsCall(op))
        {
            int token = BitConverter.ToInt32(il, 0); // Operand is a 4-byte metadata token
            MethodBase? calledMethod = baseMethod.Module.ResolveMethod(token, baseMethod.DeclaringType?.GetGenericArguments(), baseMethod.GetGenericArguments())!;
            return calledMethod;
        }

        return null;
    }

    public static MethodBase? GetCalledMethod(MethodInfo baseMethod, Instruction instruction)
    {
        return GetCalledMethod(baseMethod, instruction.OpCode, instruction.OperandIL);
    }

    public static MethodBase? GetCalledMethod(MethodInfo baseMethod, OpCode op, Byte[] il)
    {
        if (IsCall(op))
        {
            int token = BitConverter.ToInt32(il, 0); // Operand is a 4-byte metadata token
            MethodBase? calledMethod = baseMethod.Module.ResolveMethod(token, baseMethod.DeclaringType?.GetGenericArguments(), baseMethod.GetGenericArguments())!;
            return calledMethod;
        }

        return null;
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

    public static int GetRawTargetOfBranch(OpCode op, byte[] il, int pos, out int[]? switchTargets)
    {
        switchTargets = null;

        switch (op.OperandType)
        {
            case OperandType.ShortInlineBrTarget:
                {
                    int target = pos + 1 + (sbyte)il[pos]; // 1-byte signed offset, relative to NEXT instruction
                    return target;
                }
            case OperandType.InlineBrTarget:
                {
                    int offset = BitConverter.ToInt32(il, pos); // 4-byte signed offset, relative to NEXT instruction
                    int target = pos + 4 + offset;
                    return target;
                }
            case OperandType.InlineSwitch:
                {
                    int count = BitConverter.ToInt32(il, pos);
                    int baseAddr = pos + 4 + (count * 4); // end of switch operand
                    switchTargets = new int[count];
                    for (int i = 0; i < count; i++)
                    {
                        int offset = BitConverter.ToInt32(il, pos + 4 + i * 4);
                        switchTargets[i] = baseAddr + offset;
                    }
                    return switchTargets[0];
                }
        }

        return -1;
    }

    public static int NetStackDepth(MethodInfo baseMethod, Instruction instruction)
    {
        return NetStackDepth(instruction.OpCode, baseMethod, GetCalledMethod(baseMethod, instruction));
    }

    public static int NetStackDepth(OpCode opCode, MethodInfo baseMethod, MethodBase? called)
    {
        if (opCode == OpCodes.Ret || called != null && IsCall(opCode)) // box and unbox too?
        {
            int depth = MethodPushDepth(opCode, called) - MethodPopDepth(opCode, baseMethod, called);
            return depth;
        }
        else
        {
            return opCode.EvaluationStackDelta;
        }
    }

    public static int PopDepth(OpCode opCode, MethodInfo baseMethod, MethodBase? called)
    {
        int pop = opCode.StackBehaviourPop switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 => 1,
            StackBehaviour.Pop1_pop1 => 2,
            StackBehaviour.Popi => 1,
            StackBehaviour.Popi_pop1 => 2,
            StackBehaviour.Popi_popi => 2,
            StackBehaviour.Popi_popi_popi => 3,
            StackBehaviour.Popi_popi8 => 2,
            StackBehaviour.Popi_popr4 => 2,
            StackBehaviour.Popi_popr8 => 2,
            StackBehaviour.Popref => 1,
            StackBehaviour.Popref_pop1 => 2,
            StackBehaviour.Popref_popi => 2,
            StackBehaviour.Popref_popi_pop1 => 3,
            StackBehaviour.Popref_popi_popi => 3,
            StackBehaviour.Popref_popi_popi8 => 3,
            StackBehaviour.Popref_popi_popr4 => 3,
            StackBehaviour.Popref_popi_popr8 => 3,
            StackBehaviour.Popref_popi_popref => 3,
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 => 0,
            StackBehaviour.Push1_push1 => 0,
            StackBehaviour.Pushi => 0,
            StackBehaviour.Pushi8 => 0,
            StackBehaviour.Pushr4 => 0,
            StackBehaviour.Pushr8 => 0,
            StackBehaviour.Pushref => 0,
            StackBehaviour.Varpop => MethodPopDepth(opCode, baseMethod, called),
            StackBehaviour.Varpush => 0,
            _ => throw new InvalidEnumArgumentException(),
        };
        return pop;
    }

    public static int PushDepth(OpCode opCode, MethodInfo baseMethod, MethodBase? called)
    {
        int push = opCode.StackBehaviourPush switch
        {
            StackBehaviour.Pop0 => 0,
            StackBehaviour.Pop1 => 0,
            StackBehaviour.Pop1_pop1 => 0,
            StackBehaviour.Popi => 0,
            StackBehaviour.Popi_pop1 => 0,
            StackBehaviour.Popi_popi => 0,
            StackBehaviour.Popi_popi_popi => 0,
            StackBehaviour.Popi_popi8 => 0,
            StackBehaviour.Popi_popr4 => 0,
            StackBehaviour.Popi_popr8 => 0,
            StackBehaviour.Popref => 0,
            StackBehaviour.Popref_pop1 => 0,
            StackBehaviour.Popref_popi => 0,
            StackBehaviour.Popref_popi_pop1 => 0,
            StackBehaviour.Popref_popi_popi => 0,
            StackBehaviour.Popref_popi_popi8 => 0,
            StackBehaviour.Popref_popi_popr4 => 0,
            StackBehaviour.Popref_popi_popr8 => 0,
            StackBehaviour.Popref_popi_popref => 0,
            StackBehaviour.Push0 => 0,
            StackBehaviour.Push1 => 1,
            StackBehaviour.Push1_push1 => 2,
            StackBehaviour.Pushi => 1,
            StackBehaviour.Pushi8 => 1,
            StackBehaviour.Pushr4 => 1,
            StackBehaviour.Pushr8 => 1,
            StackBehaviour.Pushref => 1,
            StackBehaviour.Varpop => 0,
            StackBehaviour.Varpush => MethodPushDepth(opCode, called),
            _ => throw new InvalidEnumArgumentException(),
        };
        if (opCode == OpCodes.Leave_S)
            ;
        return push;
    }

    public static int MethodPopDepth(OpCode opCode, MethodInfo baseMethod, MethodBase? called)
    {
        if (opCode == OpCodes.Ret)
            return baseMethod.ReturnType == typeof(void) ? 0 : 1;
        else if (called == null)
            return 0;

        ParameterInfo[] prams = called.GetParameters();

        int paramCount = prams.Length;

        if (!called.IsStatic && opCode != OpCodes.Newobj)
            paramCount++; // 'this' (but not for ctor)

        return paramCount;
    }

    public static int MethodPushDepth(OpCode opCode, MethodBase? called)
    {
        if (called == null)
            return 0;

        int returns = (called is MethodInfo m && m.ReturnType != typeof(void) || opCode == OpCodes.Newobj) ? 1 : 0;
        return returns;
    }
}
