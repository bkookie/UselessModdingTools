using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace UselessModdingTools.IL;

// StackReset: The instruction after an unconditional branch will not receive any input/stack values from before the branch. All values will come from an incoming branch (or externally, for exception handlers)
public readonly record struct Instruction(OpCode OpCode, byte[] OperandIL, object? Operand, int Push, int Pop, string? Text, int Position, int Depth, Branch? Branch, bool StackReset, bool IsExceptionHandlerLead);

[DebuggerDisplay("Position = {Position,nq}, Target = {ToString(),nq}")]
public readonly record struct Branch(int Position, TargetInfo TargetInfo, TargetInfo[]? SwitchTargets) : IEnumerable<TargetInfo>
{
    public IEnumerator<TargetInfo> GetEnumerator()
    {
        if (SwitchTargets != null)
        {
            foreach (TargetInfo target in SwitchTargets)
            {
                yield return target;
            }
        }
        else
        {
            yield return TargetInfo;
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    public override readonly string ToString()
    {
        if (SwitchTargets != null)
            return $"[{string.Join(", ", SwitchTargets.Select(tar => tar.TargetPos.ToString()))}]";

        return TargetInfo.TargetPos.ToString();
    }
}

public class TargetInfo(int targetPos, int branchCount, int incomingDepth)
{
    public int TargetPos { get; } = targetPos;
    public int BranchCount { get; set; } = branchCount;
    public int IncomingDepth { get; set; } = incomingDepth;
    public int BackBranches { get; set; } = 0;

    public override string ToString()
    {
        return $"Target = {TargetPos}, Count = {BranchCount}";
    }
}

public static class BasicILScanner
{
    public static Dictionary<OpCode, OpCode> StoreLoadReciprocals { get; set; }

    private static readonly Dictionary<short, OpCode> ops = [];

    static BasicILScanner()
    {
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            OpCode op = (OpCode)field.GetValue(null)!;
            ops[op.Value] = op;
        }

        StoreLoadReciprocals = [];
        StoreLoadReciprocals[OpCodes.Stfld] = OpCodes.Ldfld;
        StoreLoadReciprocals[OpCodes.Stloc] = OpCodes.Ldloc;
        StoreLoadReciprocals[OpCodes.Stloc_0] = OpCodes.Ldloc_0;
        StoreLoadReciprocals[OpCodes.Stloc_1] = OpCodes.Ldloc_1;
        StoreLoadReciprocals[OpCodes.Stloc_2] = OpCodes.Ldloc_2;
        StoreLoadReciprocals[OpCodes.Stloc_3] = OpCodes.Ldloc_3;
        StoreLoadReciprocals[OpCodes.Stloc_S] = OpCodes.Ldloc_S;
        foreach (var pair in StoreLoadReciprocals.ToArray())
        {
            StoreLoadReciprocals[pair.Value] = pair.Key;
        }
    }

    public static bool TryReadIL(MethodInfo baseMethod, out int maxStackDepth, [NotNullWhen(true)] out List<Instruction>? instructions, [NotNullWhen(true)] out Dictionary<int, TargetInfo>? branchTargets)
    {
        string? file = baseMethod.DeclaringType?.Assembly.Location;
        if (string.IsNullOrEmpty(file))
        {
            maxStackDepth = 0;
            instructions = null;
            branchTargets = null;
            return false;
        }

        using FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using PEReader peReader = new PEReader(stream);

        MetadataReader reader = peReader.GetMetadataReader();
        MethodDefinitionHandle handle = (MethodDefinitionHandle)MetadataTokens.Handle(baseMethod.MetadataToken);
        MethodDefinition mdef = reader.GetMethodDefinition(handle);

        if (mdef.RelativeVirtualAddress != 0)
        {
            MethodBodyBlock body = peReader.GetMethodBody(mdef.RelativeVirtualAddress);
            maxStackDepth = body.MaxStack;
            return TryReadILInternal(reader, mdef, body, baseMethod, out instructions, out branchTargets);
        }

        maxStackDepth = 0;
        instructions = null;
        branchTargets = null;
        return false;
    }

    public static bool TryReadIL(MetadataReader reader, MethodDefinition mdef, MethodBodyBlock body, [NotNullWhen(true)] out List<Instruction>? instructions, [NotNullWhen(true)] out Dictionary<int, TargetInfo>? branchTargets)
    {
        return TryReadILInternal(reader, mdef, body, null, out instructions, out branchTargets);
    }

    internal static bool TryReadILInternal(MetadataReader reader, MethodDefinition mdef, MethodBodyBlock body, MethodInfo? baseMethod, [NotNullWhen(true)] out List<Instruction>? instructions, [NotNullWhen(true)] out Dictionary<int, TargetInfo>? branchTargets)
    {
        byte[]? bodyIL = body.GetILBytes();

        if (bodyIL == null)
        {
            instructions = null;
            branchTargets = null;
            return false;
        }

        instructions = [];
        branchTargets = [];
        int size;

        for (int pos = 0; pos < bodyIL.Length; pos += size)
        {
            int p = pos;

            ushort code = bodyIL[pos++];
            if (code == 0xFE)
                code = (ushort)(0xFE00 + bodyIL[pos++]);

            OpCode op = ops[(short)code];
            size = OperandSize(pos, op, bodyIL);

            byte[] il = bodyIL[pos..(pos + size)];
            int depth = 0;
            string? text = null;
            bool stackReset = false;
            bool exeption = false;
            if (instructions.Count > 0)
            {
                OpCode prevOpCode = instructions[^1].OpCode;
                if (IsUnconditionalBranch(prevOpCode))
                {
                    stackReset = true;

#warning check clausess directly; less error prone
                    if (prevOpCode == OpCodes.Leave || prevOpCode == OpCodes.Leave_S) // Exception handling
                    {
                        foreach (ExceptionRegion exRegion in body.ExceptionRegions)
                        {
                            if (exRegion.HandlerOffset == p)
                            {
                                exeption = true;

                                if (exRegion.Kind == ExceptionRegionKind.Catch || exRegion.Kind == ExceptionRegionKind.Filter)
                                {
                                    // Compiler clears the stack, then pushes the exeption onto the stack externally
                                    depth = 1;
                                    text += $" [{exRegion.CatchType}]" ?? " [Exception]";
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

            depth += NetStackDepth(reader, mdef, op, il, out int push, out int pop);

            Branch? branch = null;
            if (IsBranch(op))
            {
                int targetPos = GetTargetOfBranch(pos, op, il, out int[]? switchTargets); // Returns the highest target for switches
                TargetInfo info = null!;
                TargetInfo[]? switchInfo = null;

                if (switchTargets != null && switchTargets.Length == 0)
                    throw new InvalidOperationException(Log.ErrorMsg("Internal logic error"));

                if (switchTargets == null)
                {
                    AddTarget(branchTargets, targetPos, depth, out TargetInfo targetInfo);
                    info = targetInfo;
                }
                else
                {
                    switchInfo = new TargetInfo[switchTargets.Length];
                    for (int i = 0; i < switchTargets.Length; i++)
                    {
                        int tar = switchTargets[i];
                        AddTarget(branchTargets, tar, depth, out TargetInfo targetInfo);
                        switchInfo[i] = targetInfo;

                        if (tar == targetPos)
                            info = targetInfo;
                    }

                    if (info == null)
                        throw new InvalidOperationException(Log.ErrorMsg("Internal logic error"));
                }

                branch = new(pos, info, switchInfo);

                static void AddTarget(Dictionary<int, TargetInfo> branchTargets, int targetPos, int depth, out TargetInfo info)
                {
                    if (!branchTargets.TryGetValue(targetPos, out TargetInfo? targetInfo)) // Target depth takes the max of all incoming branch depths (but i think they should all be the same?)
                    {
                        targetInfo = new(targetPos, 1, depth);
                        branchTargets[targetPos] = targetInfo;
                    }
                    else
                    {
                        targetInfo.BranchCount++;
                        if (depth > targetInfo.IncomingDepth)
                        {
                            targetInfo.IncomingDepth = depth;
                        }
                    }
                    info = targetInfo;
                }
            }

            object? operand = (branch != null)
                ? branch
                : (baseMethod != null)
                    ? GetRuntimeOperand(baseMethod, op, il)
                    : GetMetadataOperand(reader, op, bodyIL);

            instructions.Add(new(op, il, operand, push, pop, text, p, depth, branch, stackReset, exeption));
        }
        return true;
    }

    private static int OperandSize(int pos, OpCode op, byte[] il) => op.OperandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget => 1,
        OperandType.ShortInlineI => 1,
        OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 => 8,
        OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (BitConverter.ToInt32(il, pos) * 4), // special: 4 + 4*n
        _ => 4
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

    public static bool IsFieldAccess(OpCode op) =>
        op == OpCodes.Stfld
        || op == OpCodes.Stsfld
        || op == OpCodes.Ldfld
        || op == OpCodes.Ldsfld;

    public static bool IsCall(OpCode op) =>
        op == OpCodes.Call
        || op == OpCodes.Calli
        || op == OpCodes.Callvirt
        || op == OpCodes.Newobj;

    public static bool IsBranch(OpCode op) =>
        op.OperandType == OperandType.InlineBrTarget
        || op.OperandType == OperandType.ShortInlineBrTarget
        || op.OperandType == OperandType.InlineSwitch;

    public static bool IsUnconditionalBranch(OpCode op) =>
        op == OpCodes.Br
        || op == OpCodes.Br_S
        || op == OpCodes.Leave
        || op == OpCodes.Leave_S
        || op == OpCodes.Switch;

    public static bool IsUnconditionalNonPoppingBranch(OpCode op) =>
        op == OpCodes.Br
        || op == OpCodes.Br_S
        || op == OpCodes.Leave
        || op == OpCodes.Leave_S;

    public static int GetIntFromIL(OpCode opCode, byte[] il)
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
            0x1F => (int)(sbyte)il[0], // ldc.i4.s — 1 signed byte
            0x20 => BitConverter.ToInt32(il, 0), // ldc.i4 — 4 bytes, little-endian
            0x21 => (int)BitConverter.ToInt64(il, 0), // or keep as long  // ldc.i8 — 8 bytes, little-endian
            0X22 => (int)BitConverter.ToSingle(il, 0), // ldc.r4 (float)
            0X23 => (int)BitConverter.ToDouble(il, 0), // ldc.r8 (double)
            _ => -1,
        };
    }

    public static string GetStringFromIL(Module module, byte[] il)
    {
        int token = BitConverter.ToInt32(il, 0);
        string s = module.ResolveString(token);
        return s;
    }

    public static string GetStringFromIL(MetadataReader reader, byte[] il)
    {
        try
        {
            int token = BitConverter.ToInt32(il, 0);
            UserStringHandle handle = MetadataTokens.UserStringHandle(token);
            return reader.GetUserString(handle);
        }
        catch (BadImageFormatException)
        {
            return "";
            //return $"ERR: {ex.Message}";
        }
    }

    public static int GetTargetOfBranch(int pos, OpCode op, byte[] il, out int[]? switchTargets)
    {
        switchTargets = null;

        switch (op.OperandType)
        {
            case OperandType.ShortInlineBrTarget:
                {
                    int target = pos + 1 + (sbyte)il[0]; // 1-byte signed offset, relative to NEXT instruction
                    return target;
                }
            case OperandType.InlineBrTarget:
                {
                    int offset = BitConverter.ToInt32(il, 0); // 4-byte signed offset, relative to NEXT instruction
                    int target = pos + 4 + offset;
                    return target;
                }
            case OperandType.InlineSwitch:
                {
                    int count = BitConverter.ToInt32(il, 0);
                    int baseAddr = pos + 4 + (count * 4); // end of switch operand
                    switchTargets = new int[count];
                    for (int i = 0; i < count; i++)
                    {
                        int offset = BitConverter.ToInt32(il, 4 + i * 4);
                        switchTargets[i] = baseAddr + offset;
                    }
                    return switchTargets.Max();
                }
            default:
                {
                    return -1;
                }
        }
    }

    private static object? GetRuntimeOperand(MethodInfo baseMethod, OpCode op, byte[] il)
    {
        if (IsLoadNumericConstant(op))
        {
            return GetIntFromIL(op, il);
        }
        else if (op == OpCodes.Ldstr)
        {
            return GetStringFromIL(baseMethod.Module, il);
        }
        else if (IsFieldAccess(op))
        {
            int token = BitConverter.ToInt32(il, 0);
            return baseMethod.Module.ResolveField(token, baseMethod.DeclaringType?.GetGenericArguments(), baseMethod.GetGenericArguments());
        }
        else if (IsCall(op) && op != OpCodes.Calli)
        {
            return GetCalledMethod(baseMethod, op, il);
        }

        return null;
    }

    private static object? GetMetadataOperand(MetadataReader reader, OpCode op, byte[] il)
    {
        if (IsLoadNumericConstant(op))
        {
            return GetIntFromIL(op, il);
        }
        else if (op == OpCodes.Ldstr)
        {
            return GetStringFromIL(reader, il);
        }

        return null;
    }

    private static MethodBase? GetCalledMethod(MethodInfo baseMethod, OpCode op, byte[] il)
    {
        if (IsCall(op))
        {
            int token = BitConverter.ToInt32(il, 0);
            MethodBase? calledMethod = baseMethod.Module.ResolveMethod(token, baseMethod.DeclaringType?.GetGenericArguments(), baseMethod.GetGenericArguments())!;
            return calledMethod;
        }

        return null;
    }

    private static MethodSignature<int> GetMethodSignature(MethodDefinition mdef)
    {
        MethodSignature<int> sig = mdef.DecodeSignature(new StackDeltaTypeProvider(), default);
        return sig;
    }

    private static MethodSignature<int> GetMethodSignature(MetadataReader reader, MethodDefinition? baseDef, OpCode op, byte[] il)
    {
        if (op == OpCodes.Ret)
        {
            if (baseDef == null)
                throw new ArgumentNullException(nameof(baseDef));

            return GetMethodSignature(baseDef.Value);
        }
        else if (IsCall(op))
        {
            if (op == OpCodes.Calli)
            {
                int token = BitConverter.ToInt32(il, 0);
                StandaloneSignature sig = reader.GetStandaloneSignature(MetadataTokens.StandaloneSignatureHandle(token));
                BlobReader blob = reader.GetBlobReader(sig.Signature);

                MethodSignature<int> mSig = new SignatureDecoder<int, object?>(new StackDeltaTypeProvider(), reader, null).DecodeMethodSignature(ref blob);
                return mSig;
            }
            else
            {
                int token = BitConverter.ToInt32(il, 0);
                EntityHandle calledHandle = MetadataTokens.EntityHandle(token);
                return GetMethodSignatureFromHandle(reader, calledHandle);
            }
        }

        throw new ArgumentException(Log.ErrorMsg("Cannot get a method signature from a non-calling opcode type."), nameof(op));
    }

    private static MethodSignature<int> GetMethodSignatureFromHandle(MetadataReader reader, EntityHandle calledHandle)
    {
        if (calledHandle.Kind == HandleKind.MethodDefinition)
        {
            MethodDefinition mdef = reader.GetMethodDefinition((MethodDefinitionHandle)calledHandle);
            MethodSignature<int> sig = mdef.DecodeSignature(new StackDeltaTypeProvider(), default);
            return sig;
        }
        else if (calledHandle.Kind == HandleKind.MemberReference)
        {
            MemberReference mr = reader.GetMemberReference((MemberReferenceHandle)calledHandle);
            MethodSignature<int> sig = mr.DecodeMethodSignature(new StackDeltaTypeProvider(), default);
            return sig;
        }
        else if (calledHandle.Kind == HandleKind.MethodSpecification)
        {
            MethodSpecification mspec = reader.GetMethodSpecification((MethodSpecificationHandle)calledHandle);
            EntityHandle underlying = mspec.Method; // MethodDef or MemberRef
            return GetMethodSignatureFromHandle(reader, underlying);
        }
        else
        {
            throw new ArgumentException(Log.ErrorMsg($"Invalid {nameof(HandleKind)} '{calledHandle.Kind}'"), nameof(calledHandle));
        }
    }

    private static int NetStackDepth(MetadataReader reader, MethodDefinition baseDef, OpCode op, byte[] il, out int push, out int pop)
    {
        push = PushDepth(reader, baseDef, op, il);
        pop = PopDepth(reader, baseDef, op, il);
        return push - pop;
    }

    private static int PushDepth(MetadataReader reader, MethodDefinition baseDef, OpCode op, byte[] il)
    {
        int push = op.StackBehaviourPush switch
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
            StackBehaviour.Varpush => MethodPushDepth(GetMethodSignature(reader, baseDef, op, il), op),
            _ => throw new InvalidEnumArgumentException(),
        };
        return push;
    }

    private static int PopDepth(MetadataReader reader, MethodDefinition baseDef, OpCode op, byte[] il)
    {
        int pop = op.StackBehaviourPop switch
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
            StackBehaviour.Varpop => MethodPopDepth(GetMethodSignature(reader, baseDef, op, il), op),
            StackBehaviour.Varpush => 0,
            _ => throw new InvalidEnumArgumentException(),
        };
        return pop;
    }

    private static int MethodPushDepth(MethodSignature<int> sig, OpCode op)
    {
        if (op == OpCodes.Newobj)
            return 1;

        return op == OpCodes.Newobj ? 1 : sig.ReturnType;
    }

    private static int MethodPopDepth(MethodSignature<int> sig, OpCode op)
    {
        if (op == OpCodes.Ret)
            return -sig.ReturnType; // special case

        int pop = sig.ParameterTypes.Length;

        if (op == OpCodes.Calli)
            pop++; // for the fn pointer

        if (sig.Header.Attributes.HasFlag(SignatureAttributes.Instance) && op != OpCodes.Newobj)
            pop++; // 'this' (but not for static or ctor)

        return pop;
    }

    public static string GetMethodFullName(MetadataReader reader, MethodDefinition mdef)
    {
        TypeDefinitionHandle typeHandle = mdef.GetDeclaringType();
        StringHandle tStringHandle = reader.GetTypeDefinition(typeHandle).Name;
        StringHandle mStringHandle = mdef.Name;
        string typeName = reader.GetString(tStringHandle);
        string methodName = reader.GetString(mStringHandle);
        return $"{typeName}.{methodName}";
    }

    public static string GetMethodSignatureAsString(MetadataReader reader, MethodDefinition mdef)
    {
        MethodSignature<string> sig = mdef.DecodeSignature(new StringTypeProvider(), null);
        StringBuilder sb = new();
        sb.Append(sig.ReturnType);
        sb.Append(' ');
        sb.Append(GetMethodFullName(reader, mdef));
        sb.Append('(');
        bool first = true;
        foreach (var param in sig.ParameterTypes)
        {
            if (!first)
                sb.Append(", ");

            sb.Append(param);
        }
        sb.Append(')');
        return sb.ToString();
    }
}
