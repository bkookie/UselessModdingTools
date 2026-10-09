using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Text;

namespace UselessModdingTools.IL;

//internal readonly record struct StackEntry(int DepthAfter, Instruction Instruction);

internal readonly record struct Item(int Depth, int Pos, int Indent, string Text, bool IsBranchTarget);

// Indent property seems redundant. It is identical to its index (with an offset) in the pramsToConsume list. But it has its uses. keep for now
internal readonly record struct Params(int Indent, int RemainingParams, int SourcePos, bool IsBranch, bool IsBranchInput); // RemainingParams is dual purpose; when negative, instead is the mapkey of a branchTarget channel

//internal readonly record struct Channel(ChannelType ChannelType, int value);

//internal enum ChannelType
//{
//    Parameter,
//    Branch,
//    PreBranchParam
//}

public static class ILStackVisualizer
{
    public static string ReadMethod(MethodInfo baseMethod, VisualizerMode mode, out SortedList<int, List<string>>? pipes)
    {
        pipes = null;

        if (baseMethod == null)
            return "";

        //if (baseMethod.DeclaringType?.Namespace?.StartsWith("Godot") == true)
        //    return "";

        string debugMethod = "UselessModdingTools.IL.ILStackVisualizer.ReadMethod";
        //string debugMethod = "UselessModdingTools.IL.BasicILScanner.TryReadIL";

        if ($"{baseMethod.DeclaringType}.{baseMethod.Name}".StartsWith(debugMethod))
            ;
        //else
        //    return "";

        if (!BasicILScanner.TryReadIL(baseMethod, out List<Instruction>? instructions, out Dictionary<int, TargetInfo>? branchTargets))
            return $"Error: Could not read method '{baseMethod.Name}'.\n";

        //Ignore branching for now
        if (!instructions.Select(i => i.OpCode).Any(op => BasicILScanner.IsBranch(op)))
            return "";

        //if (instructions.Count > 10)
        //    return "";

        //List<StackEntry> entries = [];
        //int depth = 0;
        int maxDepth = instructions.Max(i => i.Depth);

        //for (int i = 0; i < instructions.Count; i++)
        //{
        //    Instruction instruction = instructions[i];

        //    int depthBefore = depth;

        //    if (branchTargets.TryGetValue(instruction.Position, out int branchDepth))
        //    {
        //        //int pos = instruction.Position;
        //        //int max = instructions.Where((instr, i) => instr.Branch.HasValue && (instr.Branch.Value.Position == pos || (instr.Branch.Value.SwitchTargets?.Contains(pos) ?? false))).Max(instr => instr.Depth);
        //        //depth += max;
        //        depth += branchDepth;
        //    }
        //    else
        //    {
        //    }
        //    depth += BasicILScanner.NetStackDepth(baseMethod, instruction);

        //    entries.Add(new(instruction.Depth, instruction));
        //    if (depth > maxDepth)
        //        maxDepth = depth;
        //}

        StringBuilder sb = new();
        string methodName = $"{baseMethod.DeclaringType}.{baseMethod.Name}";
        string sig = baseMethod.ToString()!;
        int len = Math.Max(methodName.Length, sig.Length);
        sb.Append("┌─").Append(new string('─', len)).AppendLine("─┐");
        sb.Append("│ ").Append(methodName).Append(new string(' ', len - methodName.Length + 1)).AppendLine("│");
        sb.Append("├─").Append(new string('─', len)).AppendLine("─┤"); ;
        sb.Append("│ ").Append(sig).Append(new string(' ', len - sig.Length + 1)).AppendLine("│");
        sb.Append("└─").Append(new string('─', len)).AppendLine("─┘"); ;

        if (mode == VisualizerMode.StackDepth)
        {
            // Is this even useful?

            //for (int i = 0; i < entries.Count; i++)
            //{
            //    StackEntry entry = entries[i];
            //    depth = entry.Depth;
            //    sb.AppendLine($"[{depth}]{depth.Tab(entries, i - 1)}{InstructionToString(baseMethod, entry.Instruction)}");
            //}
        }
        else
        {
            Graph(sb, methodName, debugMethod, baseMethod, null, instructions, branchTargets, out pipes);
        }

        return sb.ToString();
    }

    public static string ReadMethod(MetadataReader reader, MethodDefinition mdef, MethodBodyBlock body, VisualizerMode mode, out SortedList<int, List<string>>? pipes)
    {
        pipes = null;

        TypeDefinitionHandle typeHandle = mdef.GetDeclaringType();
        StringHandle tStringHandle = reader.GetTypeDefinition(typeHandle).Name;
        StringHandle mStringHandle = mdef.Name;
        string typeName = reader.GetString(tStringHandle);
        string methodName = reader.GetString(mStringHandle);
        string fullName = $"{typeName}.{methodName}";

        //if (baseMethod == null)
        //    return "";

        //if (baseMethod.DeclaringType?.Namespace?.StartsWith("Godot") == true)
        //    return "";

        string debugMethod = "UselessModdingTools.IL.ILStackVisualizer.ReadMethod";
        //string debugMethod = "UselessModdingTools.IL.BasicILScanner.TryReadIL";

        if (debugMethod.Contains(fullName))
            ;

        //if ($"{baseMethod.DeclaringType}.{baseMethod.Name}".StartsWith(debugMethod))
        //    ;
        //else
        //    return "";

        if (!BasicILScanner.TryReadIL(reader, mdef, body, out List<Instruction>? instructions, out Dictionary<int, TargetInfo>? branchTargets))
            return $"Error: Could not read method '{fullName}'.\n";

        //Ignore branching for now
        //if (!instructions.Select(i => i.OpCode).Any(op => BasicILScanner.IsBranch(op)))
        //    return "";

        //if (instructions.Count > 10)
        //    return "";

        //List<StackEntry> entries = [];
        //int depth = 0;
        //int maxDepth = instructions.Max(i => i.Depth);

        //for (int i = 0; i < instructions.Count; i++)
        //{
        //    Instruction instruction = instructions[i];

        //    int depthBefore = depth;

        //    if (branchTargets.TryGetValue(instruction.Position, out int branchDepth))
        //    {
        //        //int pos = instruction.Position;
        //        //int max = instructions.Where((instr, i) => instr.Branch.HasValue && (instr.Branch.Value.Position == pos || (instr.Branch.Value.SwitchTargets?.Contains(pos) ?? false))).Max(instr => instr.Depth);
        //        //depth += max;
        //        depth += branchDepth;
        //    }
        //    else
        //    {
        //    }
        //    depth += BasicILScanner.NetStackDepth(baseMethod, instruction);

        //    entries.Add(new(instruction.Depth, instruction));
        //    if (depth > maxDepth)
        //        maxDepth = depth;
        //}

        StringBuilder sb = new();
        //string methodName = $"{baseMethod.DeclaringType}.{baseMethod.Name}";
        //string sig = baseMethod.ToString()!;
        //BlobReader sigBlobReader = reader.GetBlobReader(mdef.Signature);
        //sigBlobReader.ReadSignatureHeader();
        //sigBlobReader.ReadCompressedInteger();
        //string sig = baseMethod.ToString()!;
        //int len = Math.Max(methodName.Length, sig.Length);
        int len = fullName.Length;
        sb.Append("┌─").Append(new string('─', len)).AppendLine("─┐");
        sb.Append("│ ").Append(fullName).Append(new string(' ', len - fullName.Length + 1)).AppendLine("│");
        //sb.Append("├─").Append(new string('─', len)).AppendLine("─┤"); ;
        //sb.Append("│ ").Append(sig).Append(new string(' ', len - sig.Length + 1)).AppendLine("│");
        sb.Append("└─").Append(new string('─', len)).AppendLine("─┘"); ;

        if (mode == VisualizerMode.StackDepth)
        {
            // Is this even useful?

            //for (int i = 0; i < entries.Count; i++)
            //{
            //    StackEntry entry = entries[i];
            //    depth = entry.Depth;
            //    sb.AppendLine($"[{depth}]{depth.Tab(entries, i - 1)}{InstructionToString(baseMethod, entry.Instruction)}");
            //}
        }
        else
        {
            Graph(sb, fullName, debugMethod, null, reader, instructions, branchTargets, out pipes);
        }

        return sb.ToString();
    }

    private static void Graph(StringBuilder sb, string methodName, string debugMethod, MethodInfo? baseMethod, MetadataReader? reader, List<Instruction> instructions, Dictionary<int, TargetInfo> branchTargets, out SortedList<int, List<string>>? pipes)
    {
        //if (methodName.StartsWith(debugMethod))
        //    ;
        if (debugMethod.Contains(methodName))
            ;

        int maxDepth = instructions.Max(i => i.Depth);
        int maxIndent = 0;
        int channelId = 0; // Provides a unique id for channel mapping
        List<Params> pramsToConsume = [];
        List<Item> items = [];
        List<int> dupPipingIndexes = []; // used for opcodes that push multiple values (eg dup)
        Dictionary<int, int> branchMaps = []; // Key = TargetPos, Value = ChannelId
        pipes = new(instructions.Count);
        for (int i = instructions.Count - 1; i >= 0; i--)
        {
            Params? topStack = pramsToConsume.LastOrDefault();

            //StackEntry entry = entries[i];
            Instruction instruction = instructions[i];
            int pos = instruction.Position;
            //object? operand = BasicILScanner.GetOperand(reader, instruction.OpCode, instruction.OperandIL);
            //MethodBase? calledMethod = operand as MethodBase;
            int pushed = instruction.Push;
            int popped = instruction.Pop;

            int indent = pramsToConsume.Count;
            int remainingPrams = topStack.HasValue ? topStack.Value.RemainingParams : -1;
            bool nonContributor = false;

            bool isBranch = false;
            int branchChannel = 0;
            int branchInputChannelIndex = -1;
            int branchChannelIndexToRemove = -1;
            if (BasicILScanner.IsBranch(instruction.OpCode) && instruction.Branch != null)
            {
                // Need to route the pipes to the target
                //pushed = instruction.SwitchTargets?.Length ?? (instruction.Target.HasValue ? 1 : 0);

                int targetPos = instruction.Branch.Value.TargetInfo.Target;

                if (instruction.Branch.Value.SwitchTargets != null)
                {
                    // TODO: order the branches so that the channels are in the same order as the switch operands

                    // Find the left-most channel this inline switch will link with
                    for (int n = 0; n < pramsToConsume.Count - 1; n++)
                    {
                        Params pram = pramsToConsume[n];
                        foreach (TargetInfo inf in instruction.Branch.Value)
                        {
                            if (pram.RemainingParams == branchMaps[inf.Target])
                            {
                                targetPos = inf.Target;
                                n = pramsToConsume.Count; // break for;
                                break;
                            }
                        }
                    }
                }

                if (branchTargets.TryGetValue(targetPos, out TargetInfo? info))
                {
                    isBranch = true;
                    branchChannel = GetChannel(branchMaps, targetPos, ref channelId);
                    int numBranchPathsToThisTarget;

                    if (instruction.Branch.Value.SwitchTargets != null)
                    {
                        numBranchPathsToThisTarget = instruction.Branch.Value.SwitchTargets.Count(b => b.Target == targetPos); // InlineSwitch may have multiple branches for the same target
                    }
                    else
                    {
                        numBranchPathsToThisTarget = 1;
                    }

                    info.Count -= numBranchPathsToThisTarget;

                    if (targetPos < pos)
                    {
                        info.BackBranches += numBranchPathsToThisTarget;

                        int branchChannelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams < 0) + 1;
                        pramsToConsume.Insert(branchChannelIndex, new(branchChannelIndex + 1, branchChannel, SourcePos: pos, isBranch, IsBranchInput: false));

                        if (branchChannelIndex == indent)
                            indent++;
                    }
                    else if (info.Count == 0)
                    {
                        PrepareToRemoveChannel(pramsToConsume, branchChannel, out branchChannelIndexToRemove);
                    }

                    if (instruction.Depth > 0)
                    {
                        // Add a channel to visually push values onto the branch for traversal

                        branchInputChannelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams == 0);

                        if (branchInputChannelIndex >= 0 && branchInputChannelIndex > pramsToConsume.FindIndex(p => p.RemainingParams == branchChannel))
                        {
                            pramsToConsume[branchInputChannelIndex] = new(branchInputChannelIndex + 1, instruction.Depth, SourcePos: pos, IsBranch: false, IsBranchInput: true);
                        }
                        else
                        {
                            branchInputChannelIndex = pramsToConsume.Count;
                            pramsToConsume.Add(new(branchInputChannelIndex + 1, instruction.Depth, SourcePos: pos, IsBranch: false, IsBranchInput: true));
                        }

                        if (branchInputChannelIndex == indent)
                            indent++;
                    }
                }
            }

            static void PrepareToRemoveChannel(List<Params> pramsToConsume, int channel, out int channelIndexToRemove)
            {
                channelIndexToRemove = pramsToConsume.FindLastIndex(p => p.RemainingParams == channel);
            }

            bool canLinkToBranchInput = false;
            int branchInputMaxSourcePosToLinkWith = -1;
            int valuesPushedOntoBranch = 0;
            int reach = 0;
            TargetInfo? targetInfo = null;
            if (pushed > 0 && pramsToConsume.Count > 0)
            {
                int lastIndex = pramsToConsume.Count - 1;

                // Find which branches (if any) this instruction could traverse
                //int lastIndexOfBranchInput = pramsToConsume.FindLastIndex(lastIndex, p => p.RemainingParams > 0 && p.IsBranchInput);
                int remainingPush = pushed;
                int posLastPushedTo = -1; // Any branches encountered at or before this position can be traversed
                bool foundBranch = false;
                List<Params> sortedParams = [.. pramsToConsume];
                sortedParams.Sort(Comparer<Params>.Create((x, y) =>
                {
                    // Sort channels based on which will be hit first logically (as opposed to visually by following the pipes)
                    int cmp = x.SourcePos.CompareTo(y.SourcePos);
                    if (cmp != 0) return cmp;
                    return x.IsBranchInput.CompareTo(y.IsBranchInput);
                }));

                for (int m = 0; m < sortedParams.Count; m++)
                {
                    Params pram = sortedParams[m]; // 'm' now relates to the sorted list; do not use it to access the orignal list

                    if (pram.IsBranchInput && (remainingPush > 0 || pram.SourcePos <= posLastPushedTo))
                    {
                        if (!foundBranch)
                        {
                            foundBranch = true;
                            valuesPushedOntoBranch = remainingPush;
                        }

                        branchInputMaxSourcePosToLinkWith = pram.SourcePos;
                    }
                    else if (pram.RemainingParams > 0 && remainingPush > 0)
                    {
                        remainingPush -= pram.RemainingParams;
                        posLastPushedTo = pram.SourcePos;

                        if (!foundBranch && pram.IsBranch)
                        {
                            foundBranch = true;
                            valuesPushedOntoBranch = remainingPush;
                        }
                    }
                }
                canLinkToBranchInput = branchInputMaxSourcePosToLinkWith >= 0 && valuesPushedOntoBranch > 0; // false if all pushed values are popped before hitting a branch

                for (int j = 0; j < pushed; j++)
                {
                    // TODO: rework this to be more generic; just find which channels the pushed value(s) will link with. No need to worry about dup or reach
                    int k = pramsToConsume.FindLastIndex(lastIndex, p => p.RemainingParams > 0 && !p.IsBranchInput);
                    if (k >= 0)
                    {
                        if (pushed == 1)
                            reach = lastIndex - k; // How many indent levels the pushed value needs to stretch across (usually 0, but required when a branch channel gets inserted inside a param channel)

                        if (j > 0)
                        {
                            dupPipingIndexes.Add(k);
                            lastIndex = k;
                        }
                        Params tempPrams = pramsToConsume[k];
                        int pushedAmount = 1;
                        if (j == 0 && instruction.StackReset && branchTargets.TryGetValue(pos, out targetInfo))
                        {
                            // The incoming branch is not a parameter to it's target, rather it brings sibling parameter(s) alongside it's target
                            pushedAmount += targetInfo.IncomingDepth - popped;
                        }
                        pramsToConsume[k] = tempPrams with { RemainingParams = tempPrams.RemainingParams - pushedAmount };
                    }
                }
            }
            else if (pramsToConsume.Count > 0 && pramsToConsume.LastOrDefault().Indent == pramsToConsume.Count)
            {
                // Not one of the params. Ends this block
                nonContributor = true;
            }

            bool isBranchTarget = false;
            int branchTargetChannelIndex = -1;
            int branchTargetChannelIndexToRemove = -1;
            bool insertChannel = false; // if false, an empty channel was found
            Params? newBranchParams = null;
            if (targetInfo != null || branchTargets.TryGetValue(pos, out targetInfo))
            {
                isBranchTarget = true;
                int branchTargetChannel = GetChannel(branchMaps, pos, ref channelId); // channels are negative

                if (targetInfo.Count <= 0)
                {
                    // Hit by a backwards branch, and there are no forward branches
                    PrepareToRemoveChannel(pramsToConsume, branchTargetChannel, out branchTargetChannelIndexToRemove);
                }
                else
                {
                    branchTargetChannelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams == branchTargetChannel);

                    if (branchTargetChannelIndex != -1)
                    {
                        //throw new InvalidOperationException("This target already mapped???");

                        // This may be hit if an instruction is targeted by both forward and back branches
#warning Unlikely to be handled properly

                        targetInfo.Count--;
                        if (targetInfo.Count == 0)
                        {
                            newBranchParams = pramsToConsume[branchTargetChannelIndex] with { RemainingParams = 0, IsBranchInput = false };
                        }
                    }
                    else
                    {
                        branchTargetChannelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams == 0); // Find right-most empty channel

                        if (branchTargetChannelIndex != -1)
                        {
                            newBranchParams = new(Indent: branchTargetChannelIndex + 1, RemainingParams: branchTargetChannel, SourcePos: pos, IsBranch: false, IsBranchInput: false);
                        }
                        else
                        {
                            insertChannel = true;
                            branchTargetChannelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams < 0 || p.RemainingParams > 0 && p.IsBranchInput) + 1;
                            newBranchParams = new Params(Indent: branchTargetChannelIndex + 1, RemainingParams: branchTargetChannel, SourcePos: pos, IsBranch: false, IsBranchInput: false);
                            // Careful when inserting a channel, so as not to break alignments; maybe should always just add to the end.
                            // But haven't encountered such a situation; will leave the code as is until it breaks or I can confirm the correct handling.
#if DEBUG
                            if (branchTargetChannelIndex < pramsToConsume.Count)
                            {
                                throw new InvalidOperationException($"DEBUG: Inserting a channel (may break alignments). MethodName = {methodName}");
                            }
#endif
                        }

                        if (branchTargetChannelIndex == indent)
                            indent++;
                    }
                }
            }

            StringBuilder sb2 = new();
            sb2.Append("    ");
            PrintPipes(sb2, pos, instruction.Depth, popped, pushed, pramsToConsume, dupPipingIndexes, branchMaps, nonContributor,
                instruction.Branch, isBranch, branchChannel, indent,
                branchInputChannelIndex, canLinkToBranchInput, branchInputMaxSourcePosToLinkWith, valuesPushedOntoBranch, reach, isBranchTarget: false, null);
            pipes.Add(i, [$"[{instruction.Depth}] <{pos,4}> {sb2}"]);
            if (baseMethod != null)
            {
                sb2.Append(InstructionToString(baseMethod, instruction));
            }
            else if (reader != null)
            {
                sb2.Append(InstructionToString(reader, instruction));
            }
            else
            {
                throw new InvalidOperationException("No methodInfo or reader supplied");
            }
            int extraIndent = indent - pramsToConsume.Count;

            static void PrintPipes(StringBuilder sb2, int pos, int depth, int popped, int pushed, List<Params> pramsToConsume, List<int> dupPipingIndexes, Dictionary<int, int> branchMaps, bool nonContributor,
                Branch? branchInfo, bool isBranch, int branchChannel, int branchIndent,
                int branchInputChannelIndex, bool canLinkToBranchInput, int branchInputMaxSourcePosToLinkWith, int valuesPushedOntoBranch, int reach, bool isBranchTarget, TargetInfo? branchTargetInfo)
            {
                // TODO: dont mutate state in here; should just print and nothing else.

                Dictionary<int, TargetInfo>? map = null;
                if (branchInfo.HasValue)
                {
                    map = [];
                    foreach (TargetInfo info in branchInfo.Value)
                    {
                        // only used for switches?
                        map[branchMaps[info.Target]] = info;
                    }
                    //map = branchInfo.Value.ToDictionary(data => branchMaps[data.Target], data => data);
                }

                bool linkedWithChannel = false;
                bool feedingToBranchInput = false;
                bool feedingDup = false;
                bool reached = false;
                for (int j = 0; j < pramsToConsume.Count; j++)
                {
                    Params prams = pramsToConsume[j];
                    if (prams.Indent == j + 1)
                    {
                        if (j == pramsToConsume.Count - 1 && !nonContributor)
                        {
                            if (prams.RemainingParams == 0)
                            {
                                if (prams.IsBranchInput)
                                {
                                    if (feedingToBranchInput)
                                        sb2.Append("┬───");
                                    else
                                        sb2.Append("┌───");
                                }
                                else if (feedingToBranchInput || feedingDup)
                                {
                                    sb2.Append("──┬─");
                                }
                                else
                                {
                                    sb2.Append("  ┌─");
                                }
                            }
                            else
                            {
                                if (prams.IsBranchInput)
                                {
                                    if (canLinkToBranchInput && prams.SourcePos > pos)
                                    {
                                        pramsToConsume[j] = prams with { RemainingParams = prams.RemainingParams - pushed };
                                        if (feedingToBranchInput || feedingDup || reached)
                                        {
                                            if (prams.RemainingParams > 1)
                                                sb2.Append("┼───");
                                            else
                                                sb2.Append("┬───");
                                        }
                                        else
                                        {
                                            if (prams.RemainingParams > 1)
                                                sb2.Append("├───");
                                            else
                                                sb2.Append("┌───");
                                        }
                                    }
                                }
                                else if (feedingDup)
                                {
                                    sb2.Append("──┼─");
                                }
                                else if (prams.RemainingParams > 0)
                                {
                                    sb2.Append("  ├─");
                                }
                                else if (isBranchTarget && branchMaps.TryGetValue(pos, out int channel) && channel == prams.RemainingParams)
                                {
                                    sb2.Append("└───");
                                }
                                else if (prams.RemainingParams < 0 && (feedingToBranchInput || feedingDup || reached))
                                {
                                    sb2.Append("│───");
                                }
                                else
                                {
                                    sb2.Append("────");
                                }
                            }
                        }
                        else if (!feedingDup && dupPipingIndexes.Contains(j))
                        {
                            feedingDup = true;
                            if (prams.RemainingParams == 0)
                            {
                                if (feedingToBranchInput || reached)
                                    sb2.Append("──┬─");
                                else
                                    sb2.Append("  ┌─");
                            }
                            else
                            {
                                sb2.Append("  ├─");
                            }
                        }
                        else if (isBranch && branchChannel == prams.RemainingParams)
                        {
                            if (branchInfo == null)
                                throw new InvalidOperationException($"{nameof(branchInfo)} should not be null");

                            linkedWithChannel = true;
                            if (map != null && map.TryGetValue(prams.RemainingParams, out TargetInfo? targetInfo))
                            {
                                if (targetInfo.Target < branchInfo.Value.Position)
                                    sb2.Append("└───");
                                else if (targetInfo.Count == 0)
                                    sb2.Append("┌───");
                                else
                                    sb2.Append("├───");
                            }
                            else if (branchInfo.Value.TargetInfo.Target < branchInfo.Value.Position)
                                sb2.Append("└───");
                            else if (branchInfo.Value.TargetInfo.Count == 0)
                                sb2.Append("┌───");
                            else
                                sb2.Append("├───");
                        }
                        else if (prams.RemainingParams == 0)
                        {
                            if (linkedWithChannel && (isBranch || isBranchTarget) || feedingToBranchInput || feedingDup || reached)
                                sb2.Append("────");
                            else if (reach > 0 && prams.Indent == pramsToConsume.Count - reach)
                            {
                                reached = true;
                                sb2.Append("  ┌─");
                            }
                            else
                                sb2.Append("    ");
                        }
                        else if (prams.RemainingParams > 0)
                        {
                            if (isBranch && linkedWithChannel)
                            {
                                if (prams.IsBranchInput)
                                {
                                    if (canLinkToBranchInput && prams.SourcePos > pos)
                                    {
                                        pramsToConsume[j] = prams with { RemainingParams = prams.RemainingParams - pushed };
                                        sb2.Append("┼───");
                                    }
                                    else if (prams.SourcePos == pos) // Beginning of the input channel
                                        sb2.Append("┴───");
                                    else
                                        sb2.Append("│───"); // A passing input channel
                                }
                                else
                                    sb2.Append("──│─");
                            }
                            else if (prams.IsBranchInput)
                            {
                                if (canLinkToBranchInput && prams.SourcePos > pos && prams.SourcePos <= branchInputMaxSourcePosToLinkWith)
                                {
                                    pramsToConsume[j] = prams with { RemainingParams = prams.RemainingParams - valuesPushedOntoBranch };
                                    if (!(feedingToBranchInput || feedingDup || reached))
                                    {
                                        feedingToBranchInput = true;
                                        if (prams.RemainingParams > 1)
                                            sb2.Append("├───");
                                        else
                                            sb2.Append("┌───");
                                    }
                                    else
                                    {
                                        if (prams.RemainingParams > 1)
                                            sb2.Append("┼───");
                                        else
                                            sb2.Append("┬───");
                                    }
                                }
                                else if (linkedWithChannel || feedingToBranchInput || feedingDup || reached)
                                {
                                    sb2.Append("│───");
                                }
                                else
                                {
                                    sb2.Append("│   ");
                                }
                            }
                            else if (isBranchTarget)
                            {
                                if (branchIndent == prams.Indent)
                                    sb2.Append("─┐│");
                                else if (linkedWithChannel)
                                    sb2.Append("──│─");
                                else
                                    sb2.Append("  │ ");
                            }
                            else if (reach > 0 && prams.Indent == pramsToConsume.Count - reach)
                            {
                                reached = true;
                                sb2.Append("  ├─");
                            }
                            else if (feedingDup)
                            {
                                // unsure about this block
                                sb2.Append("│───");
                                //if (feedingToBranchInput || feedingDup || reached)
                                //    sb2.Append("│─│─");
                                //else
                                //    sb2.Append("│ │ ");
                            }
                            else
                            {
                                if (feedingToBranchInput || feedingDup || reached)
                                    sb2.Append("──│─");
                                else
                                    sb2.Append("  │ ");
                            }
                        }
                        else if (linkedWithChannel && (isBranch || isBranchTarget) || feedingToBranchInput)
                        {
                            if (map != null && map.TryGetValue(prams.RemainingParams, out TargetInfo? targetInfo))
                            {
                                // This should only trigger on inlineswitch
                                // Only the left-most channel will have been processed; do the remainder here
                                if (branchInfo != null && branchInfo.Value.SwitchTargets != null)
                                {
                                    targetInfo.Count -= branchInfo.Value.SwitchTargets.Count(b => b.Target == targetInfo.Target); // InlineSwitch may have multiple branches for the same target
                                }
                                else
                                {
                                    targetInfo.Count--;
                                }

                                if (targetInfo.Count == 0)
                                {
                                    pramsToConsume[j] = pramsToConsume[j] with { RemainingParams = 0, IsBranchInput = false };
                                    sb2.Append("┬───");
                                }
                                else
                                {
                                    sb2.Append("┼───");
                                }
                            }
                            else
                            {
                                sb2.Append("│───");
                            }
                        }
                        else if (isBranchTarget && branchMaps.TryGetValue(pos, out int channel) && channel == prams.RemainingParams)
                        {
                            linkedWithChannel = true;
                            if (branchTargetInfo != null && branchTargetInfo.BackBranches > 0)
                            {
                                if (branchTargetInfo.Count == 0)
                                    sb2.Append("┌───");
                                else
                                    sb2.Append("├───");
                            }
                            else
                                sb2.Append("└───");
                        }
                        else if (reached && prams.RemainingParams < 0)
                        {
                            sb2.Append("│───");
                        }
                        else
                        {
                            sb2.Append("│   ");
                        }
                    }
                    else
                    {
                        sb2.Append("    ");
                    }
                }

                // For ret in particular. Need to indent the instruction when it is sitting drectly in the channel of the incoming branch pipe (the pipe needs room to do a little dog leg)
                int remainingBranchTargetIndent = branchIndent - pramsToConsume.Count;
                for (int i = 0; i < remainingBranchTargetIndent; i++)
                {
                    if (isBranch || isBranchTarget)
                    {
                        // No more channels on the stack, so need to manually pipe across to the label
                        if (isBranchTarget && i == remainingBranchTargetIndent - 1)
                            sb2.Append("─┐");
                        else
                            sb2.Append("────");
                    }
                    else
                    {
                        sb2.Append("    ");
                    }
                }
            }
            dupPipingIndexes.Clear();

            if (popped > 0 && !instruction.StackReset && !instruction.IsExceptionHandlerLead) // The instruction leading an exeption handler consumes a value pushed by the CLR externally to the method (dont try to trace it)
            {
                pramsToConsume.Add(new(pramsToConsume.Count + 1 + extraIndent, popped, SourcePos: pos, isBranch, IsBranchInput: false));
                if (pramsToConsume.Count > maxIndent)
                    maxIndent++;
            }
            items.Add(new(instruction.Depth, instruction.Position, indent, sb2.ToString(), false));

            if (isBranch && branchChannelIndexToRemove >= 0)
            {
                pramsToConsume[branchChannelIndexToRemove] = pramsToConsume[branchChannelIndexToRemove] with { RemainingParams = 0, IsBranchInput = false };
            }

            if (isBranchTarget)
            {
                if (newBranchParams != null && branchTargetChannelIndex >= 0)
                {
                    if (insertChannel)
                        pramsToConsume.Insert(branchTargetChannelIndex, newBranchParams.Value);
                    else
                        pramsToConsume[branchTargetChannelIndex] = newBranchParams.Value;
                }
                StringBuilder sb3 = new();
                sb3.Append("    ");
                PrintPipes(sb3, pos, instruction.Depth, popped: 0, pushed: 0, pramsToConsume, dupPipingIndexes, branchMaps, nonContributor: true,
                    branchInfo: null, isBranch: false, branchChannel: 0, indent + 1,
                    branchInputChannelIndex: -1, canLinkToBranchInput: false, branchInputMaxSourcePosToLinkWith: -1, valuesPushedOntoBranch: 0, reach: 0, isBranchTarget, targetInfo);
                pipes[i].Insert(0, $"    {sb3}");
                //sb3.Append("─┐");
                items.Add(new(0, 0, branchTargetChannelIndex + 1, sb3.ToString(), isBranchTarget)); // Add this after the main entry, so will be printed above it

                if (branchTargetChannelIndexToRemove >= 0)
                {
                    pramsToConsume[branchTargetChannelIndexToRemove] = pramsToConsume[branchTargetChannelIndexToRemove] with { RemainingParams = 0, IsBranchInput = false };
                }
            }

            // Provide continuation piping (for when adding extra line gaps between instructions, but want piping to remain contigous)
            StringBuilder sb4 = new();
            sb4.Append("    ");
            for (int n = 0; n < pramsToConsume.Count; n++)
            {
                Params prams = pramsToConsume[n];
                if (prams.RemainingParams == 0)
                    sb4.Append("    ");
                else if (prams.RemainingParams < 0 || prams.IsBranchInput)
                    sb4.Append("│   ");
                else
                    sb4.Append("  │ ");
            }
            pipes[i].Insert(0, $"    {sb4}");

            while (pramsToConsume.Count > 0 && pramsToConsume.LastOrDefault().RemainingParams == 0)
            {
                // Must be done last to preserve indentation
                pramsToConsume.RemoveLast();
            }

            static int GetChannel(Dictionary<int, int> map, int targetPos, ref int channelId)
            {
                if (map.TryGetValue(targetPos, out int channel))
                    return channel;

                map[targetPos] = --channelId; // Do not store zero here
                return channelId;
            }
        }

        int maxDigits = Math.Max(0, maxDepth.ToString().Length);
        //int maxPosDigits = entries.Count > 0 ? Math.Max(0, entries[^1].Instruction.Position.ToString().Length) : 0;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            var (dep, pos, indent, str, isBranchTarget) = items[i];

            if (isBranchTarget)
            {
                sb.AppendLine($"{new string(' ', 2 + maxDigits + 1 + 2 + 4 + 1 + 2 + 4)}{str}");
            }
            else
            {
                string depfth = dep.ToString().PadLeft(maxDigits);
                sb.AppendLine($"[{depfth}] <{pos:X4}> <{pos.ToString(),4}>{str}");
            }
        }
    }

    private static string InstructionToString(MethodInfo baseMethod, Instruction instruction)
    {
        // If next target is unconditional branch, use "┤" instead?
        string or = (instruction.OpCode == OpCodes.Or) ? "┘" : ""; // Only has two letters so otherwise looks broken when connecting pipes (pipes connect to the third letter)
        return $"{instruction.OpCode}{or}{instruction.Text}{FormatOperand(BasicILScanner.GetOperand(baseMethod, instruction))}";
    }

    private static string InstructionToString(MetadataReader reader, Instruction instruction)
    {
        // If next target is unconditional branch, use "┤" instead?
        string or = (instruction.OpCode == OpCodes.Or) ? "┘" : ""; // Only has two letters so otherwise looks broken when connecting pipes (pipes connect to the third letter)
        return $"{instruction.OpCode}{or}{instruction.Text}{FormatOperand(BasicILScanner.GetOperand(reader, instruction.OpCode, instruction.OperandIL))}";
    }

    private static string? FormatOperand(object? operand)
    {
        if (operand == null)
            return null;

        if (operand is string)
            return $" \"{operand}\"".ReplaceLineEndings("\\n");

        if (operand is MethodBase method)
            return $" ---> {method}";

        if (operand is Branch branchTarget)
        {
            return $" >> {branchTarget}";
        }

        return $" {operand}";
    }
}

public enum VisualizerMode
{
    StackDepth,
    Parameters,
}

file static class Extensions
{
    extension(int depth)
    {
        public string Tab()
        {
            return new string(' ', (depth + 1) * 4);
        }

        public string Tab(List<Instruction> entries, int prevIndex)
        {
            StringBuilder sb = new();
            sb.Append("    ");

            for (int i = 0; i < depth; i++)
            {
                if (i == depth - 1 && prevIndex > 0)
                    sb.Append((entries[prevIndex].Depth < depth) ? "├───" : "│   ");
                else
                    sb.Append("│   ");
            }

            return sb.ToString();
        }
    }

    extension<T>(List<T> list)
    {
        public T Last => list[^1];

        public T RemoveLast()
        {
            T item = list[^1];
            list.RemoveAt(list.Count - 1);
            return item;
        }
    }
}
