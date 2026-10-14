# Dynamic Island for Windows

一个类似 iPhone 灵动岛的 Windows 桌面小组件:屏幕顶部居中的胶囊,显示当前正在播放的音乐,悬停即可展开控制。

## 功能(MVP)

- 通过系统媒体会话(SMTC)接入任意播放器,如 Apple Music、网易云音乐、Spotify、浏览器等
- 胶囊显示封面、标题和播放状态;鼠标悬停展开为卡片,带歌手、进度条和上一首/播放暂停/下一首
- 切歌时自动展开 3 秒
- 镜像系统通知:读取通知中心里所有应用(含微信、QQ、Telegram 等 IM 软件)的 Toast,显示应用图标、应用名、标题和正文,6 秒后自动收起
- 通知应用白名单:在托盘菜单里勾选允许的应用,并打开"仅显示选中应用的通知"
- 本地 HTTP 接口:任何程序或脚本都能向灵动岛推送一条通知(见下文),不需要包标识
- 无活动时自动隐藏;检测到全屏应用时避让
- 窗口置顶、不抢焦点、不出现在 Alt-Tab,透明区域可点穿
- 托盘菜单:通知白名单、开机自启、退出;单实例运行

## 运行

要求:Windows 10 (19041) 及以上,[.NET 10 SDK](https://dotnet.microsoft.com/download)。

只用音乐功能时,可以直接运行:

```powershell
dotnet run --project src/DynamicIsland
```

读取系统通知需要应用具备**包标识**,必须以 MSIX 方式运行(直接 `dotnet run` 时通知模块会提示启动失败,其余功能不受影响)。需要 Windows SDK(提供 `makeappx` / `signtool`)。

```powershell
# 开发:注册为松散包(需开启"开发者模式",无需签名)
scripts\package.ps1 -Register

# 分发:生成并签名 artifacts\DynamicIsland.msix
scripts\package.ps1 -Msix
```

- `-Register` 会把 `artifacts\layout` 注册为开发包,该目录需要保留;注册后从开始菜单启动 "Dynamic Island"。
- `-Msix` 使用自签名开发证书,安装前需以管理员身份把生成的 `artifacts\DynamicIsland-dev.cer` 导入"受信任的人"。
- 首次使用需在"设置 → 隐私和安全性 → 通知"中允许本应用读取通知。
- 设置保存在 `%AppData%\DynamicIsland\settings.json`;以 MSIX 运行时该路径会被重定向到 `%LocalAppData%\Packages\EkkoCheung.DynamicIsland_*\LocalCache\Roaming\DynamicIsland\settings.json`。

> 专注助手(勿扰模式)压掉的 Toast 不会进入通知中心,也就无法被读取;IM 软件若开启"隐藏消息预览",只能显示它放进 Toast 的内容。

## 本地接口

程序启动后会监听 `http://127.0.0.1:17890`,向 `/notify` POST 一条 JSON 即可在灵动岛显示通知:

```powershell
$body = @{ title = '构建完成'; body = 'dynamic-island 编译成功'; app = 'CI' } | ConvertTo-Json
Invoke-RestMethod -Uri http://127.0.0.1:17890/notify -Method Post `
  -ContentType 'application/json; charset=utf-8' -Body ([Text.Encoding]::UTF8.GetBytes($body))
```

```bash
curl -X POST http://127.0.0.1:17890/notify -H "Content-Type: application/json" \
  -d '{"title":"构建完成","body":"dynamic-island 编译成功","app":"CI","durationMs":4000}'
```

| 字段 | 说明 |
|---|---|
| `title` / `body` | 至少填一个;标题最长 200 字,正文最长 1000 字 |
| `app` | 显示的来源名称,默认 `API` |
| `durationMs` | 显示时长,默认 6000,范围 1000 到 30000 |

成功返回 `200 {"ok":true}`;错误返回 `{"ok":false,"error":"..."}`,状态码为 400(参数错误)、404(路径不对)、405(不是 POST)、413(请求体超过 64 KB)、415(Content-Type 不是 `application/json`)。

- 只监听本机回环地址,且只接受 `application/json`,网页脚本无法在不触发跨域预检的情况下调用它。
- 接口发来的通知不受应用白名单限制。
- 端口在设置文件的 `ApiPort` 里修改,设为 `0` 关闭接口;端口被占用时启动会提示"本地接口模块启动失败"。
- 向非本机暴露、鉴权、图标和进度条暂不支持。

## 架构

```
src/DynamicIsland/
  Core/        活动抽象:IslandActivity、IActivityProvider、ActivityHub
  Providers/   活动来源:Media(SMTC)、Notifications(UserNotificationListener)、Api(本地 HTTP)
  Settings/    JSON 设置(通知白名单等)
  Shell/       胶囊窗口、视图模型、窗口原生调用
  Views/       各类活动对应的视图
scripts/       打包脚本(package.ps1)
```

`ActivityHub` 汇总所有 Provider 推送的活动,按 `Priority` 选出当前显示的一个,同级时最近更新者优先。新增一类活动的步骤:

1. 实现一个 `IActivityProvider`,推送自己的 `IslandActivity`
2. 在 `IslandWindow.xaml` 里为该活动类型加一个 `DataTemplate`
3. 在 `App.xaml.cs` 里用 `hub.AddProviderAsync(...)` 注册

## 路线图

- [x] 系统通知 / IM 通知(MSIX 包标识 + `UserNotificationListener`)
- [x] 通知应用白名单与设置持久化
- [x] App 通知:本地 HTTP 接入点(`POST /notify`)
- [ ] 长任务进度显示
- [ ] 歌词显示(SMTC 不提供歌词,需接入第三方歌词服务)
- [ ] 多显示器选择

## 许可证

[MIT](LICENSE)
