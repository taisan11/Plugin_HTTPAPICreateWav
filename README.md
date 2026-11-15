# Plugin\_HTTPAPICreateWav

このPluginは、HTTP APIを利用して、Wavファイルを作成するためのPluginです。
主にYMM4-MoreBOUYOMIPlugin用ですが、HTTPを経由して他の人も使えるようになってます。

さらに、rawmodeに対応して直接aquestalk記法を送り込めるようになりました!!

直接DLLを触ってるわけではないのでそれよりは白よりなグレーで済んでます。

## APIの使い方

### 話者一覧（1.2.0以降）

`GET http://localhost:50088/getvoicelist` で、利用可能な声のIDと名前をJSONで取得できます。
音声生成と同じポートを使用するため、棒読みちゃん本体のHTTPサーバーは不要です。
`text` や `filename` は不要です。APIパスワードを設定している場合は、このAPIにも認証が必要です。

```json
{"voiceList":[{"id":1,"kind":"AquesTalk","name":"女性1","alias":""}]}
```

各要素は `id`（`voice` に指定するID）、`kind`（エンジン種別）、`name`（声の名前）、
`alias`（本体で設定した別名）を持ちます。ID順に返し、ID 0（本体で選択中の声）は含めません。
応答のContent-Typeは `application/json; charset=utf-8` です。

### WAV生成

`/speak` は通常の読み上げ変換、`/speakraw` はAquesTalkの音声記号列をそのままWAVにします。
GETまたはPOSTのクエリで指定してください（POST本文の解析はしません）。

```text
http://localhost:50088/speak?text=こんにちは&filename=C%3A%5Ctemp%5Cvoice.wav
```

- `text` と `filename` は必須です。保存先フォルダーは事前に作成し、拡張子は `.wav` にしてください。クエリの値はURLエンコードします。
- `speed`: 50〜300、`volume`: 0〜300、`tone`: 50〜200。`-1` は本体設定を使用します。省略時は順に `-1`、`-1`、`100` です。
- `voice`: 省略時は `0`（本体で選択中の声）。利用できない声や不正な入力は400を返します。
- `/speakraw` はAquesTalk系の声のみ対応します。`speed=-1` は本体の現在の速度を使います。音量・音程は加工しないため、`volume` / `tone` は省略または `-1` / `100` のみ指定できます。
- 通常モードはそのリクエストのWAV保存完了を待ちます。30秒で504を返しますが、本体のタスク自体はキャンセルされません。指定先へのコピーは行わず、`.httpwav-*.wav` / `.tmp` が残る場合があります。

### パスワード

プラグイン設定の「APIパスワード」で設定できます。**デフォルトは空（認証なし）**です。
設定した場合は `X-API-Password` ヘッダー、または `password` クエリで送信してください。不一致・未指定は401になります。

パスワードは設定ファイルに平文保存されます。クエリはURL履歴などにも残るため、ヘッダーでの送信を推奨します。
保存先の制限はしていないので、信頼できるクライアントからのみ使用してください。

## dotnet CLIでのビルド

### 必要なもの

- Windows と .NET SDK（`dotnet` コマンド）
- 棒読みちゃん本体の `BouyomiChan.exe`
- 初回のNuGetパッケージ復元に必要なインターネット接続

ターゲットは **.NET Framework 3.5** です。

### ビルド手順

リポジトリ直下に `lib` フォルダーを作り、棒読みちゃん本体の `BouyomiChan.exe` をコピーしてください。

リポジトリ直下で実行します。

```powershell
dotnet build -c Release
```

本体をコピーせず、参照先を指定することもできます。パスには絶対パスを推奨します。

```powershell
dotnet build -c Release -p:BouyomiChanPath="C:\path\to\BouyomiChan.exe"
```

生成されるプラグインは `Plugin_HTTPAPICreateWav/bin/Release/Plugin_HTTPAPICreateWav.dll` です。棒読みちゃんの実行ファイルと同じフォルダーに配置して使用してください。

`dotnet build` のみならDebugビルドになります。x86を指定する場合は `-p:Platform=x86` を追加してください。
