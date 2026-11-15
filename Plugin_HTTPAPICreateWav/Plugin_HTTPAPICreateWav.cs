using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Net;
using System.Collections.Specialized;
using System.Collections.Generic;
using FNF.JsonParser;
using FNF.Utility;
using FNF.XmlSerializerSetting;
using FNF.BouyomiChanApp;

namespace Plugin_HTTPAPICreateWav
{
    public class Plugin_HTTPAPICreateWav : IPlugin
    {
        private Settings_HTTPAPICreateWav _Settings;
        private SettingFormData_HTTPAPICreateWav _SettingFormData;
        private string _SettingFile = Base.CallAsmPath + Base.CallAsmName + ".setting";
        private HttpListener _Server;
        private volatile bool _Stopping;

        public string Name { get { return "HTTP wave保存"; } }
        public string Version { get { return "1.2.0"; } }
        public string Caption { get { return "HTTPを使っていい感じにwavファイルを作って保存します。"; } }
        public ISettingFormData SettingFormData { get { return _SettingFormData; } }

        public void Begin()
        {
            _Settings = new Settings_HTTPAPICreateWav(this);
            _Settings.Load(_SettingFile);
            _SettingFormData = new SettingFormData_HTTPAPICreateWav(_Settings);
            if (_Settings.Port < 1 || _Settings.Port > 65535)
                throw new ArgumentOutOfRangeException("Port", "ポートは1〜65535で指定してください。");

            _Stopping = false;
            _Server = new HttpListener();
            _Server.Prefixes.Add(string.Format("http://localhost:{0}/", _Settings.Port));
            _Server.Start();
            _Server.BeginGetContext(HttpListenerCallback, _Server);
        }

        public void End()
        {
            _Stopping = true;
            if (_Server != null)
            {
                _Server.Close();
                _Server = null;
            }
            if (_Settings != null)
                _Settings.Save(_SettingFile);
        }

        private void HttpListenerCallback(IAsyncResult result)
        {
            HttpListener server = (HttpListener)result.AsyncState;
            HttpListenerContext context = null;
            try
            {
                context = server.EndGetContext(result);
                server.BeginGetContext(HttpListenerCallback, server);
            }
            catch (Exception ex)
            {
                if (context != null) context.Response.Close();
                if (!_Stopping) Console.WriteLine("HTTP受付エラー: " + ex.Message);
                return;
            }

            try
            {
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;
                string path = request.Url.AbsolutePath;
                if (path != "/speak" && path != "/speakraw" && path != "/getvoicelist")
                {
                    WriteResponse(response, 404, "Not Found");
                    return;
                }
                // X-API-Passwordをブラウザーから送る場合のプリフライト。
                if (request.HttpMethod == "OPTIONS")
                {
                    response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
                    response.Headers["Access-Control-Allow-Headers"] = "X-API-Password, Content-Type";
                    WriteResponse(response, 204, "");
                    return;
                }
                if (request.HttpMethod != "GET" && request.HttpMethod != "POST")
                {
                    response.Headers["Allow"] = "GET, POST, OPTIONS";
                    WriteResponse(response, 405, "Use GET or POST.");
                    return;
                }

                NameValueCollection query = HttpUtilityEx.ParseQuery(request.RawUrl, true);
                string password = request.Headers["X-API-Password"] ?? query["password"];
                if (!string.IsNullOrEmpty(_Settings.Password) && _Settings.Password != password)
                {
                    WriteResponse(response, 401, "Invalid password.");
                    return;
                }

                if (path == "/getvoicelist")
                {
                    WriteResponse(response, 200, CreateVoiceListJson(Pub.FormMain.BC.Voices), "application/json");
                    return;
                }

                string text = query["text"];
                string filename = query["filename"];
                if (string.IsNullOrEmpty(text) || text.Trim().Length == 0 || text.IndexOf('\0') >= 0)
                    throw new ArgumentException("textを指定してください（空白のみ・NUL文字は不可）。");
                if (string.IsNullOrEmpty(filename) || filename.Trim().Length == 0)
                    throw new ArgumentException("filenameを指定してください。");
                filename = Path.GetFullPath(filename);
                if (Path.GetFileName(filename).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                    || !string.Equals(Path.GetExtension(filename), ".wav", StringComparison.OrdinalIgnoreCase)
                    || !Directory.Exists(Path.GetDirectoryName(filename)))
                    throw new ArgumentException("filenameには存在するフォルダー内の.wavファイルを指定してください。");

                int speed = ReadNumber(query, "speed", -1, 50, 300);
                int volume = ReadNumber(query, "volume", -1, 0, 300);
                int tone = ReadNumber(query, "tone", 100, 50, 200);
                int voiceId = ReadNumber(query, "voice", 0, 0, int.MaxValue);
                if (voiceId < 0) throw new ArgumentException("voiceは0以上で指定してください。");
                var bc = Pub.FormMain.BC;
                VoiceType voice = (VoiceType)voiceId;
                // 本体と同じ旧ID互換変換。存在しない声への暗黙フォールバックはしない。
                if (!bc.Voices.ContainsKey(voice))
                {
                    if (voiceId >= 9 && voiceId <= 999) voice = (VoiceType)(10001 + voiceId - 9);
                    else if (voiceId >= 1001 && voiceId <= 1008) voice = (VoiceType)(voiceId - 1000);
                }
                if (!bc.Voices.ContainsKey(voice))
                    throw new ArgumentException("指定されたvoiceは利用できません。");

                bool raw = path == "/speakraw";
                if (raw)
                {
                    if (!(bc.Voices[voice] is BouyomiChan.AquesTalkVoiceBase))
                        throw new ArgumentException("speakrawはAquesTalk系のvoiceのみ対応しています。");
                    if ((volume != -1 && volume != 100) || (tone != -1 && tone != 100))
                        throw new ArgumentException("speakrawではvolume/toneは変更できません。省略または-1/100を指定してください。");
                    speed = speed == -1 ? Math.Max(50, Math.Min(300, bc.Speed)) : speed;
                    AddTalkRawTask(text, speed, voice, filename);
                }
                else
                {
                    AddTalkTask(text, speed, volume, voice, tone, filename);
                }
                string message = string.Format("text={0}, speed={1}, volume={2}, voice={3}, tone={4}",
                    text, speed, volume, (int)voice, tone);
                if (raw) message += ", raw=true, volume/tone=not_applied";
                WriteResponse(response, 200, message);
            }
            catch (ArgumentException ex) { TryWriteError(context.Response, 400, ex.Message); }
            catch (NotSupportedException ex) { TryWriteError(context.Response, 400, ex.Message); }
            catch (PathTooLongException ex) { TryWriteError(context.Response, 400, ex.Message); }
            catch (TimeoutException ex) { TryWriteError(context.Response, 504, ex.Message); }
            catch (Exception ex)
            {
                Console.WriteLine("HTTP音声生成エラー: " + ex);
                TryWriteError(context.Response, 500, "音声生成またはファイル保存に失敗しました。");
            }
            finally { context.Response.Close(); }
        }

        // 本体のHTTPサーバーを有効にしなくても、WAV生成と同じ接続先で話者を取得できる。
        private static string CreateVoiceListJson(IDictionary<VoiceType, BouyomiChan.IVoice> voices)
        {
            var sortedVoices = new List<KeyValuePair<VoiceType, BouyomiChan.IVoice>>(voices);
            sortedVoices.Sort((a, b) => a.Key.CompareTo(b.Key));
            var voiceList = new JsonArray();
            foreach (var entry in sortedVoices)
            {
                // ID 0は本体で選択中の声の別名なので、一覧には含めない。
                if (entry.Key == VoiceType.Default) continue;
                var voice = entry.Value;
                string kind;
                if (voice is BouyomiChan.VoiceSAPI50) kind = "SAPI5";
                else if (voice is BouyomiChan.VoiceSPPF) kind = "Speech Platform";
                else if (voice is BouyomiChan.AquesTalkFX) kind = "AquesTalkFX";
                else if (voice is BouyomiChan.AquesTalk2) kind = "AquesTalk2";
                else if (voice is BouyomiChan.AquesTalkVoiceBase) kind = "AquesTalk";
                else kind = "Unknown";

                var item = new JsonObject();
                item.Add("id", new JsonNumber((int)entry.Key));
                item.Add("kind", new JsonString(kind));
                item.Add("name", new JsonString(voice.Name ?? string.Empty));
                item.Add("alias", new JsonString(voice.Data.Alias ?? string.Empty));
                voiceList.ArrayEx.Add(item);
            }
            var result = new JsonObject();
            result.Add("voiceList", voiceList);
            return result.ToString();
        }

        private static int ReadNumber(NameValueCollection query, string name, int defaultValue, int min, int max)
        {
            if (query[name] == null) return defaultValue;
            int value;
            if (!int.TryParse(query[name], out value) || (value != -1 && (value < min || value > max)))
                throw new ArgumentException(string.Format("{0}は{1}〜{2}の整数または-1で指定してください。", name, min, max));
            return value;
        }

        private static void WriteResponse(HttpListenerResponse response, int status, string message, string contentType = "text/plain")
        {
            response.StatusCode = status;
            response.ContentType = contentType + "; charset=utf-8";
            response.ContentEncoding = Encoding.UTF8;
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Cache-Control"] = "no-store";
            byte[] buffer = Encoding.UTF8.GetBytes(message);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
        }

        private static void TryWriteError(HttpListenerResponse response, int status, string message)
        {
            try { WriteResponse(response, status, "Error: " + message); }
            catch { /* 切断済みのクライアントには応答できない */ }
        }

        // Beta21は保存完了時に.wav.tmpを.wavへリネームするので、固有パスでそれを待つ。
        private void AddTalkTask(string text, int speed, int volume, VoiceType voice, int tone, string filename)
        {
            string stagingFile = Path.Combine(Path.GetDirectoryName(filename), ".httpwav-" + Guid.NewGuid().ToString("N") + ".wav");
            try
            {
                if (Pub.AddTalkTask(text, speed, tone, volume, voice, stagingFile) < 0)
                    throw new InvalidOperationException("棒読みちゃんがタスクを受け付けませんでした。");
                Stopwatch timer = Stopwatch.StartNew();
                while (!File.Exists(stagingFile))
                {
                    if (_Stopping) throw new InvalidOperationException("プラグインを停止しました。");
                    if (timer.ElapsedMilliseconds >= 30000)
                        throw new TimeoutException("WAV保存が30秒以内に完了しませんでした。");
                    Thread.Sleep(20);
                }
                if (_Stopping) throw new InvalidOperationException("プラグインを停止しました。");
                if (new FileInfo(stagingFile).Length <= 44)
                    throw new InvalidOperationException("生成されたWAVに音声データがありません。");
                File.Copy(stagingFile, filename, true);
            }
            finally
            {
                // タイムアウトしても指定先には公開しない。処理中の一時ファイルは残る場合がある。
                try { File.Delete(stagingFile); File.Delete(stagingFile + ".tmp"); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        // 変換・音量/音程調整を通さず、エンジンの生WAVを保存する。
        private void AddTalkRawTask(string text, int speed, VoiceType voice, string filename)
        {
            var voiceObj = (BouyomiChan.AquesTalkVoiceBase)Pub.FormMain.BC.Voices[voice];
            int size;
            IntPtr wavPtr = voiceObj.CreateWavData(text, speed, out size);
            try
            {
                if (wavPtr == IntPtr.Zero || size <= 44)
                    throw new InvalidOperationException("AquesTalkの音声生成に失敗しました。音声記号列を確認してください。");
                byte[] wavData = new byte[size];
                Marshal.Copy(wavPtr, wavData, 0, size);
                File.WriteAllBytes(filename, wavData);
            }
            finally
            {
                if (wavPtr != IntPtr.Zero) voiceObj.FreeWavData(wavPtr);
            }
        }

        public class Settings_HTTPAPICreateWav : SettingsBase
        {
            public int Port { get; set; } = 50088;
            public string Password { get; set; } = string.Empty;
            internal Plugin_HTTPAPICreateWav Plugin;
            public Settings_HTTPAPICreateWav() { }
            public Settings_HTTPAPICreateWav(Plugin_HTTPAPICreateWav plugin) { Plugin = plugin; }
            public override void ReadSettings() { }
            public override void WriteSettings() { }
        }

        public class SettingFormData_HTTPAPICreateWav : ISettingFormData
        {
            Settings_HTTPAPICreateWav _Setting;
            public string Title { get { return _Setting.Plugin.Name; } }
            public bool ExpandAll { get { return false; } }
            public SettingsBase Setting { get { return _Setting; } }
            public SettingFormData_HTTPAPICreateWav(Settings_HTTPAPICreateWav setting)
            {
                _Setting = setting;
                PBase = new SBase(_Setting);
            }
            public SBase PBase;
            public class SBase : ISettingPropertyGrid
            {
                Settings_HTTPAPICreateWav _Setting;
                public SBase(Settings_HTTPAPICreateWav setting) { _Setting = setting; }
                public string GetName() { return "HTTP設定"; }
                [Category("基本設定")]
                [DisplayName("ポートを変更する")]
                [Description("使用するポートを変更します（再起動後に反映）")]
                public int Port { get { return _Setting.Port; } set { _Setting.Port = value; } }
                [Category("基本設定")]
                [DisplayName("APIパスワード")]
                [Description("空なら認証なし。X-API-Passwordヘッダーまたはpasswordクエリで送信します。設定ファイルには平文保存されます。")]
                [PasswordPropertyText(true)]
                public string Password { get { return _Setting.Password; } set { _Setting.Password = value ?? string.Empty; } }
            }
        }
    }
}
