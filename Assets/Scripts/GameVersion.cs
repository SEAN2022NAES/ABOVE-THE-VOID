/// <summary>
/// 版本号唯一来源（single source of truth）：
///   - 游戏内所有版本号展示（主页左下角常驻版本行）一律读这里的常量，不再各自写死字符串；
///   - 打包版本号 PlayerSettings.asset 的 bundleVersion 在运行时读不到，故打版时必须与本文件的
///     Number 手工保持一致（两处同改，再 git tag 同名版本）；
///   - 升级版本只改 Number 一处，Display / 主页展示会自动跟着变。
/// </summary>
public static class GameVersion
{
    /// <summary>语义化版本号（不带 v 前缀），与 PlayerSettings 的 bundleVersion 保持一致。</summary>
    public const string Number = "0.3.0";

    /// <summary>带 v 前缀的展示用版本号，如 v0.3.0。</summary>
    public const string Display = "v" + Number;
}
