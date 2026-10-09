using System.Reflection;
using System.Text;

namespace UselessModdingTools.IL;

//internal readonly record struct StackEntry(int DepthAfter, Instruction Instruction);

internal readonly record struct Item(int Depth, int Pos, int Indent, string Text);
internal readonly record struct Params(int Indent, int RemainingParams);

public static class ILStackVisualizer
{
    public static string ReadMethod(MethodInfo baseMethod, VisualizerMode mode)
    {
        if (baseMethod.DeclaringType?.Namespace?.StartsWith("Godot") == true)
            return "";

        string debugMethod = "UselessModdingTools.IL.BasicILScanner.GetOperand";

        if ($"{baseMethod.DeclaringType}.{baseMethod.Name}".StartsWith(debugMethod))
            ;

        if (!BasicILScanner.TryReadIL(baseMethod, out var instructions, out var branchTargets))
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
            List<Params> pramsToConsume = [];
            List<Item> items = [];
            List<int> dupIndexes = []; // used for opcodes that push multiple values (eg dup)
            for (int i = instructions.Count - 1; i >= 0; i--)
            {
                Params? topStack = pramsToConsume.LastOrDefault();

                //StackEntry entry = entries[i];
                Instruction instruction = instructions[i];
                object? operand = BasicILScanner.GetOperand(baseMethod, instruction);
                MethodBase? calledMethod = operand as MethodBase;
                int pushed = BasicILScanner.PushDepth(instruction.OpCode, baseMethod, calledMethod);
                int popped = BasicILScanner.PopDepth(instruction.OpCode, baseMethod, calledMethod);

                int indent = pramsToConsume.Count;
                int remainingPrams = topStack.HasValue ? topStack.Value.RemainingParams : -1;
                bool nonContributor = false;

                if (BasicILScanner.IsBranch(instruction.OpCode))
                {
                    // Need to route the pipes to the target
                    //pushed = instruction.SwitchTargets?.Length ?? (instruction.Target.HasValue ? 1 : 0);
                }

                if (pushed > 0 && pramsToConsume.Count > 0)
                {
                    int lastIndex = pramsToConsume.Count - 1;
                    for (int j = 0; j < pushed; j++)
                    {
                        int k = pramsToConsume.FindLastIndex(lastIndex, p => p.RemainingParams > 0);
                        if (k != pramsToConsume.Count - 1)
                        {
                            for (int l = k; l <= lastIndex; l++)
                            {
                                dupIndexes.Add(l);
                            }
                            lastIndex = k;
                        }
                        Params tempPrams = pramsToConsume[k];
                        pramsToConsume[k] = tempPrams with { RemainingParams = tempPrams.RemainingParams - 1 };
                    }
                }
                else if (pramsToConsume.Count > 0 && pramsToConsume.LastOrDefault().Indent == pramsToConsume.Count)
                {
                    // Not one of the params (not directly anyway). Ends this block
                    nonContributor = true;
                }

                StringBuilder sb2 = new();
                sb2.Append("    ");

                for (int j = 0; j < pramsToConsume.Count; j++)
                {
                    Params prams = pramsToConsume[j];
                    if (prams.Indent == j + 1)
                    {
                        if (j == pramsToConsume.Count - 1 && !nonContributor)
                        {
                            if (prams.RemainingParams == 0)
                            {
                                if (dupIndexes.Contains(j))
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
                                if (dupIndexes.Contains(j))
                                {
                                    sb2.Append("──┼─"); // Impossible?
                                }
                                else
                                {
                                    sb2.Append("  ├─");
                                }
                            }
                        }
                        else if (dupIndexes.Contains(j))
                        {
                            if (prams.RemainingParams == 0)
                            {
                                if (dupIndexes.Contains(j - 1))
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
                        else if (prams.RemainingParams == 0)
                        {
                            sb2.Append("    ");
                        }
                        else
                        {
                            sb2.Append("  │ ");
                        }
                    }
                    else
                    {
                        sb2.Append("    ");
                    }
                }
                dupIndexes.Clear();

                if (popped > 0 && !instruction.IsExceptionHandlerLead) // The instruction leading an exeption handler consumes a value pushed by the CLR externally to the method (dont try to trace it)
                {
                    pramsToConsume.Add(new(pramsToConsume.Count + 1, popped));
                    if (pramsToConsume.Count > maxIndent)
                        maxIndent++;
                }

                while (pramsToConsume.Count > 0 && pramsToConsume.LastOrDefault().RemainingParams == 0)
                {
                    // Must be done last to preserve indentation
                    pramsToConsume.RemoveLast();
                }

                sb2.Append(InstructionToString(baseMethod, instruction));
                items.Add(new(instruction.Depth, instruction.Position, indent, sb2.ToString()));
            }

            int maxDigits = Math.Max(0, maxDepth.ToString().Length);
            //int maxPosDigits = entries.Count > 0 ? Math.Max(0, entries[^1].Instruction.Position.ToString().Length) : 0;

            for (int i = items.Count - 1; i >= 0; i--)
            {
                var (dep, pos, indent, str) = items[i];
                string depfth = dep.ToString().PadLeft(maxDigits);
                //int digits = dept.Length;

                sb.AppendLine($"[{depfth}] <{pos.ToString(),3}>{str}");
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

        //if (operand is Switch sw)
        //    return $" >> [{string.Join(", ", sw.Targets.Select(i => i.ToString()))}]";

        if (operand is Branch branchTarget)
        {
            if (branchTarget.SwitchTargets != null)
                return $" >> [{string.Join(", ", branchTarget.SwitchTargets.Select(i => i.ToString()))}]"; // Should not hit, should be a Switch object

            return $" >> {branchTarget.Target}";
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
