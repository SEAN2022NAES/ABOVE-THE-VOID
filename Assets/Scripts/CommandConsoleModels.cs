/// <summary>
/// 指令框数据模型（类 / 结构体分支结构）：
///   - CommandType：指令类型枚举，新增指令在此追加分支；
///   - CommandData：解析后的指令数据（结构体：类型 + 原文 + 参数数组）；
///   - CommandParser：把输入文本解析为 CommandData 的静态解析器。
/// 与 CommandConsoleController 解耦，便于后续扩展更多指令。
/// </summary>

/// <summary>指令类型枚举。新增指令时在此追加枚举值并在解析器 / 执行器补充分支。</summary>
public enum CommandType
{
    Unknown,     // 无法识别的指令
    Help,        // help / 帮助
    Laser,       // laser / 发射激光（占位预留）
    Teleport,    // tp / teleport / 传送（占位预留）
    TimeScale    // timescale / 时间流速（占位预留）
}

/// <summary>解析后的指令数据（结构体：指令类型 + 原始文本 + 参数列表）。</summary>
public struct CommandData
{
    public CommandType Type;
    public string Raw;
    public string[] Args;

    /// <summary>构造一条「未知指令」数据（用于解析失败兜底）。</summary>
    public static CommandData UnknownOf(string raw)
    {
        CommandData data;
        data.Type = CommandType.Unknown;
        data.Raw = raw ?? string.Empty;
        data.Args = new string[0];
        return data;
    }
}

/// <summary>
/// 指令解析器：按「命令字 -> 枚举」的分支结构把输入文本解析为 CommandData。
/// 每个 case 即一个可扩展分支；命令字大小写不敏感，支持中英文别名。
/// </summary>
public static class CommandParser
{
    /// <summary>解析一条原始指令文本。</summary>
    public static CommandData Parse(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return CommandData.UnknownOf(raw ?? string.Empty);

        string trimmed = raw.Trim();
        string[] tokens = trimmed.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return CommandData.UnknownOf(trimmed);

        string head = tokens[0].ToLowerInvariant();
        string[] args = new string[tokens.Length - 1];
        System.Array.Copy(tokens, 1, args, 0, args.Length);

        CommandType type;
        switch (head)
        {
            case "help":
            case "帮助":
                type = CommandType.Help;
                break;
            case "laser":
            case "发射激光":
                type = CommandType.Laser;
                break;
            case "tp":
            case "teleport":
            case "传送":
                type = CommandType.Teleport;
                break;
            case "timescale":
            case "时间流速":
                type = CommandType.TimeScale;
                break;
            default:
                type = CommandType.Unknown;
                break;
        }

        CommandData data;
        data.Type = type;
        data.Raw = trimmed;
        data.Args = args;
        return data;
    }
}
