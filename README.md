# CS2 BigHead

一个基于 CounterStrikeSharp 的 CS2 玩家模型缩放插件，支持自由调节**头部**和**身体**大小，真人玩家和 Bot 均可生效。

---

## 快速上手（直接作为独立插件使用）

### 1. 编译与安装
```sh
dotnet build BigHead.csproj -c Release
```
编译完成后，将 `bin/Release/net10.0/` 目录下的这三个文件：
* `BigHead.dll`
* `BigHead.pdb`
* `BigHead.deps.json`

丢进服务器的插件目录：  
`game/csgo/addons/counterstrikesharp/plugins/BigHead/` 即可。

### 2. 控制台指令
指令目前仅对执行命令的玩家本人生效：

* `css_bighead <倍率>` —— 调整头部大小（例如 `css_bighead 2` 变成双倍大头）
* `css_bigbody <倍率>` —— 调整身体大小（例如 `css_bigbody 1.5`）
* `css_bighead reset` / `css_bigbody reset` —— 恢复默认大小
* `css_bighead status` / `css_bigbody status` —— 查看当前缩放倍率

---

## 二次开发（集成到你的插件中）

如果你想在自己的插件逻辑中动态修改玩家大小：

### 环境依赖
* **.NET 10**
* **CounterStrikeSharp.API** `>= 1.0.378`

### 接入方式
1. 直接把 [`PlayerScaleService.cs`](source/BigHead/PlayerScaleService.cs) 拷进你的项目工程。
2. 绑定生命周期：在插件的 `Load` 中初始化，在 `Unload` 中释放。

```csharp
using BigHead;

public class MyPlugin : BasePlugin
{
    private PlayerScaleService _scales;

    public override void Load(bool hotReload)
    {
        _scales = new PlayerScaleService(this);

        // 在你的事件或业务逻辑中调用
        // 比如给某个玩家开大头模式：
        _scales.TrySetHeadScale(player, 2.0f);
        _scales.TrySetBodyScale(player, 1.25f);
    }

    public override void Unload(bool hotReload)
    {
        _scales?.Dispose();
    }
}
```

### 常用 API 说明
* **设置大小**：`TrySetHeadScale(player, scale)` / `TrySetBodyScale(player, scale)`（成功返回 `true`，玩家无效时返回 `false`）
* **读取倍率**：`GetHeadScale(player)` / `GetBodyScale(player)`
* **单项重置**：`ResetHeadScale(player)` / `ResetBodyScale(player)`
* **全量重置**：`ResetPlayer(player)`（头身同时恢复默认）

---

## 注意事项

1. **主线程调用**：必须在游戏主线程使用，且保持单例即可。
2. **倍率限制**：数值必须是大于 0 的有限正数（不支持负数或 0）。
3. **状态保留**：设置倍率后，玩家在**复活时会自动重新应用**，换图时会自动清空。
4. **碰撞体积与视角**：身体缩放**仅修改视觉模型**，不会改变角色的行走碰撞框（Collision）以及第一人称眼高。

---

## 开源协议

本项目基于 [MIT](LICENSE) 协议开源。
