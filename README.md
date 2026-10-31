# cs2-css-bighead

用 CounterStrikeSharp 调整 CS2 玩家头部和身体大小，支持人类和 Bot。

## 接入插件

需要 .NET 10、CounterStrikeSharp.API 1.0.378+。

把 [PlayerScaleService.cs](source/BigHead/PlayerScaleService.cs) 加入项目，在 `Load` 中创建 `scales = new PlayerScaleService(this)`，在 `Unload` 中调用 `scales.Dispose()`。

```csharp
using BigHead;

// 在事件或业务逻辑中按条件设置
scales.TrySetHeadScale(player, 2f);
scales.TrySetBodyScale(player, 1.25f);
```

用 `GetHeadScale` / `GetBodyScale` 读取倍率，`ResetHeadScale` / `ResetBodyScale` 分别恢复，`ResetPlayer` 全部恢复。

在游戏主线程使用一个服务实例。倍率须为有限正数；玩家不可用时 `TrySet` 返回 `false`。设置后自动在复活时重新应用，换图清空。身体缩放只调整模型，不另外修改行走碰撞或眼高。

## 独立运行

```sh
dotnet build BigHead.csproj -c Release
```

将 `bin/Release/net10.0/` 中的 `BigHead.dll`、`BigHead.pdb`、`BigHead.deps.json` 放入 `game/csgo/addons/counterstrikesharp/plugins/BigHead/`。

控制台使用 `css_bighead 2` 或 `css_bigbody 1.5`；参数换成 `reset` 恢复，`status` 查看当前倍率。命令只修改自己。

## 许可

[MIT](LICENSE)
