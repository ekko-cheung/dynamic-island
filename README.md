# Dynamic Island for Windows

一个类似 iPhone 灵动岛的 Windows 桌面小组件:屏幕顶部居中的胶囊,显示当前正在播放的音乐,悬停即可展开控制。

## 功能(MVP)

- 通过系统媒体会话(SMTC)接入任意播放器,如 Apple Music、网易云音乐、Spotify、浏览器等
- 胶囊显示封面、标题和播放状态;鼠标悬停展开为卡片,带歌手、进度条和上一首/播放暂停/下一首
- 切歌时自动展开 3 秒
- 无媒体会话时自动隐藏;检测到全屏应用时避让
- 窗口置顶、不抢焦点、不出现在 Alt-Tab,透明区域可点穿
- 托盘菜单:开机自启、退出;单实例运行

## 运行

要求:Windows 10 (19041) 及以上,[.NET 10 SDK](https://dotnet.microsoft.com/download)。

```powershell
dotnet run --project src/DynamicIsland
```

## 架构

```
src/DynamicIsland/
  Core/        活动抽象:IslandActivity、IActivityProvider、ActivityHub
  Providers/   活动来源,目前只有 Media(SMTC)
  Shell/       胶囊窗口、视图模型、窗口原生调用
  Views/       各类活动对应的视图
```

`ActivityHub` 汇总所有 Provider 推送的活动,按 `Priority` 选出当前显示的一个,同级时最近更新者优先。新增一类活动的步骤:

1. 实现一个 `IActivityProvider`,推送自己的 `IslandActivity`
2. 在 `IslandWindow.xaml` 里为该活动类型加一个 `DataTemplate`
3. 在 `App.xaml.cs` 里用 `hub.AddProviderAsync(...)` 注册

## 路线图

- [ ] 系统通知(`UserNotificationListener` 需要应用具备包标识,需单独评估)
- [ ] App 通知:本地 named pipe / HTTP 接入点
- [ ] 长任务进度显示
- [ ] 歌词显示(SMTC 不提供歌词,需接入第三方歌词服务)
- [ ] 设置持久化、多显示器选择

## 许可证

[MIT](LICENSE)
