# 麻雀学习仓库

本仓库用于保存雀魂牌谱、通用化牌谱数据、复盘记录和后续分析程序。

当前包含一个基于 .NET 10 的雀魂牌谱导入器。它会：

1. 从雀魂分享链接提取牌谱编号；
2. 通过当前 CN WebGL 的 Route/Lobby Protobuf 协议读取牌谱；
3. 保存雀魂原始二进制数据；
4. 转换为与平台无关、适合 Git 管理的 JSON 事件流；
5. 以 SHA-256 关联原始数据和转换结果。

## 环境

- .NET SDK 10
- 雀魂国服账号

雀魂目前会对 `fetchGameRecord` 返回错误码 `1004`，直到连接完成登录。因此仅有分享链接和玩家昵称不足以下载近期牌谱。导入器支持在本机终端隐藏输入密码，不会把账号或密码写入文件或日志。

## 导入牌谱

首次使用：

```powershell
dotnet restore
dotnet run --project src/Maque.Cli -- import --login --player Zackson "https://game.maj-soul.com/1/?paipu=260826-826cd976-c7b5-4ef5-8c80-3fbf91f95a0b_a21920067" "https://game.maj-soul.com/1/?paipu=260826-29701cc4-cdde-4c4a-925f-53e084b9ff66_a21920067" "https://game.maj-soul.com/1/?paipu=260823-be706326-22da-4f44-8a40-b0de261de110_a21920067"
```

程序将依次询问登录账号和密码。这里的登录账号不一定等于游戏昵称 `Zackson`。

也可以显式指定账号：

```powershell
dotnet run --project src/Maque.Cli -- import --account "你的登录账号" --player Zackson "牌谱链接"
```

自动任务可以使用以下进程环境变量：

- `MAJSOUL_ACCOUNT`
- `MAJSOUL_PASSWORD`
- `MAJSOUL_RESOURCE_VERSION`
- `MAJSOUL_PACKAGE_VERSION`

不要将包含密码的 `.env`、脚本或配置提交到 Git。本仓库已经忽略 `.env` 和 `config.local.json`。

## 数据目录

```text
data/
├── records/                         # 通用 JSON 牌谱
│   └── <牌谱编号>.json
└── raw/
    └── majsoul/                     # 雀魂原始 Protobuf
        └── <牌谱编号>.bin
```

通用格式见 [docs/universal-paipu-format.md](docs/universal-paipu-format.md)。

## 构建和测试

```powershell
dotnet build Maque.slnx
dotnet test Maque.slnx
```

## 协议兼容性

雀魂没有为第三方提供稳定的公开牌谱 API，客户端协议可能更新。导入器当前依据：

- 旧版官方入口的 `version.json` 和 `config.json` 进行线路发现；
- 当前 Unity WebGL 标题页显示的资源版本 `0.16.273`；
- 当前 WebGL 包版本 `4.0.46`；
- Liqi 的 Route/Lobby Protobuf 帧格式。

若登录返回错误码 `151`，通常意味着资源版本变化。可从雀魂标题页右下角读取新版本，并通过 `--resource-version` 指定，例如：

```powershell
dotnet run --project src/Maque.Cli -- import --login --resource-version 0.16.274 "牌谱链接"
```

程序只用于本人牌谱的离线学习和复盘，不参与实时对局操作。
