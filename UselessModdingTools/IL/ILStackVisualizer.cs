using System.Reflection;
using System.Text;

namespace UselessModdingTools.IL;

//internal readonly record struct StackEntry(int DepthAfter, Instruction Instruction);

internal readonly record struct Item(int Depth, int Pos, int Indent, string Text, bool IsBranchTarget);
internal readonly record struct Params(int Indent, int RemainingParams); // RemainingParams is dual purpose; when negative, instead is the mapkey of a branchTarget channel
#warning Indent property seems redundant. It is identical to its index in the pramsToConsume list

public static class ILStackVisualizer
{
    public static string ReadMethod(MethodInfo baseMethod, VisualizerMode mode)
    {
        if (baseMethod.DeclaringType?.Namespace?.StartsWith("Godot") == true)
            return "";

        string debugMethod = "UselessModdingTools.IL.BasicILScanner.GetOperand";

        if ($"{baseMethod.DeclaringType}.{baseMethod.Name}".StartsWith(debugMethod))
            ;

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
            //for (int i = 0; i < entries.Count; i++)
            //{
            //    StackEntry entry = entries[i];
            //    depth = entry.Depth;
            //    sb.AppendLine($"[{depth}]{depth.Tab(entries, i - 1)}{InstructionToString(baseMethod, entry.Instruction)}");
            //}
        }
        else
        {
            if (methodName.StartsWith(debugMethod))
                ;

            int maxIndent = 0;
            int channelId = 0; // Provides a unique id for channel mapping
            List<Params> pramsToConsume = [];
            List<Item> items = [];
            List<int> dupPipingIndexes = []; // used for opcodes that push multiple values (eg dup)
            Dictionary<int, int> branchMaps = []; // Key = TargetPos, Value = ChannelId
            for (int i = instructions.Count - 1; i >= 0; i--)
            {
                Params? topStack = pramsToConsume.LastOrDefault();

                //StackEntry entry = entries[i];
                Instruction instruction = instructions[i];
                int pos = instruction.Position;
                object? operand = BasicILScanner.GetOperand(baseMethod, instruction);
                MethodBase? calledMethod = operand as MethodBase;
                int pushed = BasicILScanner.PushDepth(instruction.OpCode, baseMethod, calledMethod);
                int popped = BasicILScanner.PopDepth(instruction.OpCode, baseMethod, calledMethod);

                int indent = pramsToConsume.Count;
                int remainingPrams = topStack.HasValue ? topStack.Value.RemainingParams : -1;
                bool nonContributor = false;
                int channelIndex = 0; // need to be more clear about the distintion between this and branchChannelIndex

                bool insertChannel = false;
                Params? branchParams = null;

                bool isBranch = false;
                int branchChannel = 0;
                int branchChannelIndex = 0;
                Params? channelToRemove = null; // misnomer; stores an empty channel that will replace an existing channel (used with branchChannelIndex). not even necessary (just need the index)
                Params? channelToInsert = null;
                if (BasicILScanner.IsBranch(instruction.OpCode) && instruction.Branch != null)
                {
                    // Need to route the pipes to the target
                    //pushed = instruction.SwitchTargets?.Length ?? (instruction.Target.HasValue ? 1 : 0);

                    int targetPos = instruction.Branch.Value.TargetInfo.Target;

                    if (instruction.Branch.Value.SwitchTargets != null)
                    {
                        // Find the left-most channel this inline switch will link with
                        for (int n = pramsToConsume.Count - 1; n >= 0; n--)
                        {
                            Params pram = pramsToConsume[n];
                            foreach (TargetInfo inf in instruction.Branch.Value)
                            {
                                if (pram.RemainingParams == branchMaps[inf.Target])
                                {
                                    targetPos = inf.Target;
                                    break;
                                }
                            }
                        }
                    }

                    if (branchTargets.TryGetValue(targetPos, out TargetInfo? info))
                    {
                        isBranch = true;
                        branchChannel = GetChannel(branchMaps, targetPos, ref channelId);
                        int numBranchPaths;

                        if (instruction.Branch.Value.SwitchTargets != null)
                        {
                            numBranchPaths = instruction.Branch.Value.SwitchTargets.Count(b => b.Target == targetPos); // InlineSwitch may have multiple branches for the same target
                        }
                        else
                        {
                            numBranchPaths = 1;
                        }

                        info.Count -= numBranchPaths;

                        if (targetPos < pos)
                        {
                            info.BackBranches += numBranchPaths;

                            channelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams < 0) + 1;
                            pramsToConsume.Insert(channelIndex, new(channelIndex + 1, branchChannel));

                            if (channelIndex == indent)
                                indent++;
                        }
                        else if (info.Count == 0)
                        {
                            PrepareToRemoveChannel(pramsToConsume, branchChannel, out channelIndex, out branchChannelIndex, out channelToRemove);
                        }
                    }
                }

                static void PrepareToRemoveChannel(List<Params> pramsToConsume, int branchChannel, out int channelIndex, out int branchChannelIndex, out Params? channelToRemove)
                {
                    channelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams == branchChannel);
                    if (channelIndex >= 0)
                    {
                        branchChannelIndex = channelIndex;
                        channelToRemove = pramsToConsume[channelIndex] with { RemainingParams = 0 };
                    }
                    else
                    {
                        branchChannelIndex = -1;
                        channelToRemove = null;
                    }
                }

                if (pushed > 0 && pramsToConsume.Count > 0
                    && (i == pramsToConsume.Count - 1 || !BasicILScanner.IsUnconditionalNonPoppingBranch(instructions[i + 1].OpCode))) // Cannot push because they do not pop
                {
                    int lastIndex = pramsToConsume.Count - 1;
                    for (int j = 0; j < pushed; j++)
                    {
                        int k = pramsToConsume.FindLastIndex(lastIndex, p => p.RemainingParams > 0);
                        if (k >= 0)
                        {
                            if (k != pramsToConsume.Count - 1)
                            {
                                for (int l = k; l <= lastIndex; l++)
                                {
                                    dupPipingIndexes.Add(l);
                                }
                                lastIndex = k;
                            }
                            Params tempPrams = pramsToConsume[k];
                            pramsToConsume[k] = tempPrams with { RemainingParams = tempPrams.RemainingParams - 1 };
                        }
                    }
                }
                else if (pramsToConsume.Count > 0 && pramsToConsume.LastOrDefault().Indent == pramsToConsume.Count)
                {
                    // Not one of the params (not directly anyway). Ends this block
                    nonContributor = true;
                }

                bool isBranchTarget = false;
                channelIndex = 0; // No longer needed from above
                if (branchTargets.TryGetValue(pos, out TargetInfo? targetInfo))
                {
                    if (isBranch)
                    {
#warning not currently supported well
                        ;
                        //throw new NotSupportedException($"<{methodName} pos:{pos}> Graph does not support an item that is both a branch source and target");
                    }

                    isBranchTarget = true;
                    branchChannel = GetChannel(branchMaps, pos, ref channelId); // channels are negative

                    if (targetInfo.Count <= 0)
                    {
                        PrepareToRemoveChannel(pramsToConsume, branchChannel, out channelIndex, out branchChannelIndex, out channelToRemove);
                    }
                    else
                    {
                        // Calculate but dont edit anything until this instruction has been processed (cant upset the channels)
                        channelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams == branchChannel);

                        //int channelIndent;

                        if (channelIndex != -1)
                        {
                            //throw new InvalidOperationException("This target already mapped???");

                            // This may be hit if an instruction is targeted by both forward and back branches
#warning Unlikely to be handled properly

                            targetInfo.Count--;
                            if (targetInfo.Count == 0)
                            {
                                //pramsToConsume[channelIndex] = pramsToConsume[channelIndex] with { RemainingParams = 0 };
                                branchParams = pramsToConsume[channelIndex] with { RemainingParams = 0 };
                            }
                        }
                        else
                        {
                            channelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams == 0);

                            if (channelIndex != -1)
                            {
                                //pramsToConsume[channelIndex] = pramsToConsume[channelIndex] with { RemainingParams = channel };
                                branchParams = pramsToConsume[channelIndex] with { Indent = indent, RemainingParams = branchChannel };
                            }
                            else
                            {
                                channelIndex = pramsToConsume.FindLastIndex(p => p.RemainingParams < 0) + 1;
                                //if (channelIndex == -1) channelIndex = 0;
                                branchParams = new Params(channelIndex + 1, branchChannel); // with Indent = indent?
                                insertChannel = true;
                                //pramsToConsume.Insert(channelIndex, new Params(channelIndex + 1, channel));
                            }

                            if (channelIndex == indent) // while <= indent?
                                indent++;

                            //channelIndent = channelIndex + 1;
                        }
                    }

                    //if (pramsToConsume.Count >= 2 && pramsToConsume[^2].RemainingParams == 0)
                    //{
                    //    pramsToConsume[^2] = pramsToConsume[^2] with { RemainingParams = -channel };
                    //}
                    //else
                    //{
                    //    pramsToConsume.Insert(0, new Params(indent, -channel));
                    //    //indent++;
                    //}
                    //StringBuilder sb3 = new();
                    //sb3.Append("    ");
                    //PrintPipes(sb3, pos, pramsToConsume, dupPipingIndexes, branchMaps, false);
                    //sb3.Append("─┐");
                    //branchItem = new(0, 0, channelIndex + 1, sb3.ToString());
                }

                StringBuilder sb2 = new();
                sb2.Append("    ");
                PrintPipes(sb2, pos, pramsToConsume, dupPipingIndexes, branchMaps, nonContributor, instruction.Branch, isBranch, branchChannel, indent, isBranchTarget: false, null);
                int extraIndent = indent - pramsToConsume.Count;
                if (!pramsToConsume.Any(p => p.RemainingParams >= 0))
                {
                    // For ret in particular. Need to indent the instruction when it is sitting drectly in the channel of the incoming branch pipe (the pipe needs room to do a little dog leg)
                    for (int m = 0; m < extraIndent; m++)
                        sb2.Append("    ");
                }
                sb2.Append(InstructionToString(baseMethod, instruction));

                static void PrintPipes(StringBuilder sb2, int pos, List<Params> pramsToConsume, List<int> dupPipingIndexes, Dictionary<int, int> branchMaps, bool nonContributor, Branch? branchInfo, bool isBranch, int branchChannel, int branchIndent, bool isBranchTarget, TargetInfo? branchTargetInfo)
                {
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
                    for (int j = 0; j < pramsToConsume.Count; j++)
                    {
                        Params prams = pramsToConsume[j];
                        if (prams.Indent == j + 1)
                        {
                            if (j == pramsToConsume.Count - 1 && !nonContributor)
                            {
                                if (prams.RemainingParams == 0)
                                {
                                    if (dupPipingIndexes.Contains(j))
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
                                    if (dupPipingIndexes.Contains(j))
                                    {
                                        sb2.Append("──┼─"); // Impossible?
                                    }
                                    else if (prams.RemainingParams > 0)
                                    {
                                        sb2.Append("  ├─");
                                    }
                                    else if (isBranchTarget && branchMaps.TryGetValue(pos, out int channel) && channel == prams.RemainingParams)
                                    {
                                        sb2.Append("└───");
                                    }
                                    else
                                    {
                                        sb2.Append("────");
                                    }
                                }
                            }
                            else if (dupPipingIndexes.Contains(j))
                            {
                                if (prams.RemainingParams == 0)
                                {
                                    if (dupPipingIndexes.Contains(j - 1))
                                    {
                                        sb2.Append("────");
                                    }
                                    else
                                    {
                                        sb2.Append("  ┌─");
                                    }
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
                                if (branchInfo.Value.TargetInfo.Target < branchInfo.Value.Position)
                                    sb2.Append("└───");
                                else if (branchInfo.Value.TargetInfo.Count == 0)
                                    sb2.Append("┌───");
                                else
                                    sb2.Append("├───");
                            }
                            else if (prams.RemainingParams == 0)
                            {
                                if (linkedWithChannel && (isBranch || isBranchTarget))
                                    sb2.Append("────");
                                else
                                    sb2.Append("    ");
                            }
                            else if (prams.RemainingParams > 0)
                            {
                                if (isBranch && linkedWithChannel)
                                {
                                    sb2.Append("──│─");
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
                                else
                                {
                                    sb2.Append("  │ ");
                                }
                            }
                            else if (linkedWithChannel && (isBranch || isBranchTarget))
                            {
                                if (map != null && map.TryGetValue(prams.RemainingParams, out TargetInfo? targetInfo))
                                {
                                    // This should only trigger on inlineswitch
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
                                        pramsToConsume[j] = pramsToConsume[j] with { RemainingParams = 0 };
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

                    if (isBranch || isBranchTarget)
                    {
                        int remainingBranchTargetIndent = branchIndent - pramsToConsume.Count;
                        for (int i = 0; i < remainingBranchTargetIndent; i++)
                        {
                            // No more channels on the stack, so need to manually pipe across to the label
                            if (isBranchTarget && i == remainingBranchTargetIndent - 1)
                                sb2.Append("─┐");
                            else
                                sb2.Append("────");
                        }
                    }
                }
                dupPipingIndexes.Clear();

                if (popped > 0 && !instruction.IsExceptionHandlerLead) // The instruction leading an exeption handler consumes a value pushed by the CLR externally to the method (dont try to trace it)
                {
                    pramsToConsume.Add(new(pramsToConsume.Count + 1 + extraIndent, popped));
                    if (pramsToConsume.Count > maxIndent)
                        maxIndent++;
                }

                items.Add(new(instruction.Depth, instruction.Position, indent, sb2.ToString(), false));

                if (isBranch)
                {
                    if (channelToRemove != null)
                    {
                        pramsToConsume[branchChannelIndex] = channelToRemove.Value;
                    }
                }

                if (isBranchTarget)
                {
                    if (branchParams != null)
                    {
                        if (insertChannel)
                            pramsToConsume.Insert(channelIndex, branchParams.Value);
                        else
                            pramsToConsume[channelIndex] = branchParams.Value;
                    }
                    StringBuilder sb3 = new();
                    sb3.Append("    ");
                    PrintPipes(sb3, pos, pramsToConsume, dupPipingIndexes, branchMaps, true, null, isBranch: false, 0, indent + 1, isBranchTarget, targetInfo);
                    //sb3.Append("─┐");
                    items.Add(new(0, 0, channelIndex + 1, sb3.ToString(), isBranchTarget)); // Add this after the main entry

                    if (channelToRemove != null)
                    {
                        pramsToConsume[branchChannelIndex] = channelToRemove.Value;
                    }
                }

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
                    sb.AppendLine($"{new string(' ', 2 + maxDigits + 1 + 2 + 4)}{str}");
                }
                else
                {
                    string depfth = dep.ToString().PadLeft(maxDigits);
                    sb.AppendLine($"[{depfth}] <{pos.ToString(),4}>{str}");
                }
            }
        }

        return sb.ToString();
    }

    private static string InstructionToString(MethodInfo baseMethod, Instruction instruction)
    {
        return $"{instruction.OpCode}{instruction.Text}{FormatOperand(BasicILScanner.GetOperand(baseMethod, instruction))}";
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
