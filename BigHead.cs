// SPDX-License-Identifier: MIT
using System.Globalization;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;

namespace BigHead;

// Optional standalone host. Integrations only need PlayerScaleService.cs.
[MinimumApiVersion(378)]
public sealed class BigHeadPlugin : BasePlugin
{
    public override string ModuleName => "Big Head";
    public override string ModuleVersion => "1.2.0";
    public override string ModuleAuthor => "unicbm";
    public override string ModuleDescription => "Personal head/body scale; embeddable CSSharp service";

    private PlayerScaleService scales = null!;

    public override void Load(bool hotReload)
    {
        scales = new PlayerScaleService(this);
        AddCommand("css_bighead", "Head <positive scale|reset|status>; no argument toggles 2x", (p, c) => Control(p, c, false));
        AddCommand("css_bigbody", "Body <positive scale|reset|status>; no argument toggles 2x", (p, c) => Control(p, c, true));
        // Retain the existing local test controls; they are not the integration API.
        AddCommand("css_bighead_grow", "Head grow: 1 pressed, 0 released", (p, c) => Hold(p, c, true, false));
        AddCommand("css_bighead_shrink", "Head shrink: 1 pressed, 0 released", (p, c) => Hold(p, c, false, false));
        AddCommand("css_bigbody_grow", "Body grow: 1 pressed, 0 released", (p, c) => Hold(p, c, true, true));
        AddCommand("css_bigbody_shrink", "Body shrink: 1 pressed, 0 released", (p, c) => Hold(p, c, false, true));
    }

    public override void Unload(bool hotReload) => scales?.Dispose();

    private static bool IsHuman(CCSPlayerController? player) =>
        player is { IsValid: true, IsBot: false, IsHLTV: false, ControllingBot: false };

    private void Control(CCSPlayerController? player, CommandInfo command, bool body)
    {
        if (!IsHuman(player))
        {
            command.ReplyToCommand("[BigHead] 请以人类玩家执行；不修改接管的 Bot。");
            return;
        }
        string label = body ? "全身" : "头部";
        string name = body ? "css_bigbody" : "css_bighead";
        string arg = command.ArgCount == 1 ? (scales.IsManaged(player!, body) ? "reset" : "2") : command.GetArg(1);
        if (command.ArgCount > 2)
        {
            command.ReplyToCommand($"[BigHead] {name} <倍率|reset|status>；不带参数切换两倍/恢复。");
            return;
        }
        if (arg == "reset" || arg == "1")
        {
            if (body) scales.ResetBodyScale(player!); else scales.ResetHeadScale(player!);
            command.ReplyToCommand($"[BigHead] 已恢复{label}。");
            return;
        }
        if (arg == "status")
        {
            float? actual = body ? scales.GetBodyScale(player!) : scales.GetHeadScale(player!);
            command.ReplyToCommand(actual is { } value
                ? $"[BigHead] 当前{label}倍率 {value:G6}x。"
                : "[BigHead] 当前没有可用的存活角色。");
            return;
        }
        if (!float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out float scale)
            || !float.IsFinite(scale) || scale <= 0)
        {
            command.ReplyToCommand("[BigHead] 倍率必须是有限正数，例如 1.5、2、5；reset 恢复。");
            return;
        }
        bool applied = body ? scales.TrySetBodyScale(player!, scale) : scales.TrySetHeadScale(player!, scale);
        command.ReplyToCommand(applied
            ? $"[BigHead] {label}倍率 {scale:G6}x。"
            : "[BigHead] 角色需存活，且人物模型/游戏规则已就绪。");
    }

    private void Hold(CCSPlayerController? player, CommandInfo command, bool grow, bool body)
    {
        if (!IsHuman(player)) return;
        if (command.ArgCount != 2 || command.GetArg(1) is not ("0" or "1"))
        {
            command.ReplyToCommand("[BigHead] 按下传 1，松开传 0。");
            return;
        }
        bool pressed = command.GetArg(1) == "1";
        if (!scales.TryHold(player!, grow, pressed, body))
        {
            if (pressed) command.ReplyToCommand("[BigHead] 角色需存活，且人物模型/游戏规则已就绪。");
            return;
        }
        if (!pressed)
            command.ReplyToCommand($"[BigHead] {(body ? "全身" : "头部")}倍率 {scales.ManagedScale(player!, body):G6}x。");
    }
}
