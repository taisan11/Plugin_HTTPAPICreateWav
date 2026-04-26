using System;
using System.IO;
using System.Text;
using System.Threading;
using System.ComponentModel;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using FNF.Utility;
using FNF.XmlSerializerSetting;
using FNF.BouyomiChanApp;
using System.Net;
using System.Collections.Specialized;

namespace Plugin_HTTPAPICreateWav
{

    public class Plugin_HTTPAPICreateWav : IPlugin
    {
        #region ■フィールド

        private Settings_HTTPAPICreateWav _Settings;
        private SettingFormData_HTTPAPICreateWav _SettingFormData;
        private string _SettingFile = Base.CallAsmPath + Base.CallAsmName + ".setting";
        private HttpListener _Server;

        #endregion

        #region ■IPluginメンバの実装

        public string Name { get { return "HTTP wave保存"; } }
        public string Version { get { return "1.0.0"; } }
        public string Caption { get { return "HTTPを使っていい感じにwavファイルを作って保存します。"; } }
        public ISettingFormData SettingFormData { get { return _SettingFormData; } }

        public void Begin()
        {
            _Settings = new Settings_HTTPAPICreateWav(this);
            _Settings.Load(_SettingFile);
            _SettingFormData = new SettingFormData_HTTPAPICreateWav(_Settings);

            _Server = new HttpListener();
            if (_Settings.Port > 0)
                _Server.Prefixes.Add(string.Format("http://{0}:{1}/", "localhost", _Settings.Port));
            _Server.Start();
            _Server.BeginGetContext(new AsyncCallback(this.HttpListenerCallback), _Server);
        }

        public void End()
        {
            _Settings.Save(_SettingFile);

            if (_Server != null)
            {
                _Server.Stop();
                _Server.Close();
                _Server = null;
            }
        }

        #endregion

        #region ■HTTPリクエスト処理

        private void HttpListenerCallback(IAsyncResult result)
        {
            if (_Server == null || !_Server.IsListening) return;

            HttpListenerContext context = null;
            try
            {
                context = _Server.EndGetContext(result);
                _Server.BeginGetContext(new AsyncCallback(HttpListenerCallback), _Server);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (Exception ex)
            {
                Console.WriteLine("HTTPコンテキスト取得中にエラー: " + ex.Message);
                return;
            }

            try
            {
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;
                string path = request.Url.AbsolutePath;

                string responseString = "OK";

                if (path == "/speak" || path == "/speakraw")
                {
                    NameValueCollection queryParams = HttpUtilityEx.ParseQuery(request.RawUrl, true);

                    string text = queryParams["text"];
                    string filename = queryParams["filename"];
                    int speed = int.Parse(queryParams["speed"] ?? "-1");
                    int volume = int.Parse(queryParams["volume"] ?? "-1");
                    int voice = int.Parse(queryParams["voice"] ?? "0");
                    int tone = int.Parse(queryParams["tone"] ?? "100");

                    responseString = string.Format(
                        "text={0}, speed={1}, volume={2}, voice={3}, tone={4}",
                        text, speed, volume, voice, tone);

                    if (path == "/speak")
                        AddTalkTask(text, speed, volume, (FNF.Utility.VoiceType)voice, tone, filename);
                    else
                        AddTalkRawTask(text, speed, volume, (FNF.Utility.VoiceType)voice, tone, filename);
                }
                else
                {
                    responseString = "Not Found";
                    response.StatusCode = 404;
                }

                response.ContentType = "text/plain";
                response.ContentEncoding = Encoding.UTF8;
                response.Headers.Add("Access-Control-Allow-Origin", "*");

                byte[] buffer = Encoding.UTF8.GetBytes(responseString);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Console.WriteLine("HTTPレスポンス処理中にエラー: " + ex.Message);
                try
                {
                    if (context != null)
                    {
                        context.Response.StatusCode = 500;
                        byte[] buffer = Encoding.UTF8.GetBytes("Error: " + ex.Message);
                        context.Response.ContentLength64 = buffer.Length;
                        context.Response.OutputStream.Write(buffer, 0, buffer.Length);
                        context.Response.OutputStream.Close();
                    }
                }
                catch { }
            }
        }

        #endregion

        #region ■発話タスク

        // 棒読みちゃんの変換パイプラインを通す（漢字→読み仮名変換あり）
        private void AddTalkTask(string text, int speed, int volume, FNF.Utility.VoiceType voice, int tone, string filename)
        {
            Pub.AddTalkTask(text, speed, tone, volume, voice, filename);
            WaitForTaskCompletion();
        }

        // 変換パイプラインを完全バイパス（AquesTalk記法をそのまま渡す）
        //
        // AquesTalkVoiceBase.CreateWavData() を直接呼び出す。
        // 文節分解・漢字変換など全ての前処理を通さず、DLLの生出力をそのまま保存する。
        //
        // textにはAquesTalk記法を渡す。例:
        //   AquesTalk1系 → "ko'nnichiwa"
        //   AquesTalk2系 → "コンニチワ" または phoneme記法
        private void AddTalkRawTask(string text, int speed, int volume, FNF.Utility.VoiceType voice, int tone, string filename)
        {
            var bc = Pub.FormMain.BC;

            // AquesTalk系以外（SAPI5, SPPF）はバイパス不可のためフォールバック
            if (!bc.Voices.ContainsKey(voice) || !(bc.Voices[voice] is BouyomiChan.AquesTalkVoiceBase))
            {
                Console.WriteLine(string.Format(
                    "[speakraw] voice={0} はAquesTalk系でないためフォールバックします（変換パイプラインが走ります）", voice));
                AddTalkTask(text, speed, volume, voice, tone, filename);
                return;
            }

            var voiceObj = (BouyomiChan.AquesTalkVoiceBase)bc.Voices[voice];

            // speed=-1 のときは棒読みちゃんのデフォルト値を使う
            int actualSpeed = (speed == -1) ? 100 : speed;

            int size;
            IntPtr wavPtr = voiceObj.CreateWavData(text, actualSpeed, out size);

            if (wavPtr == IntPtr.Zero || size <= 0)
            {
                throw new InvalidOperationException(string.Format(
                    "[speakraw] AquesTalk音声合成に失敗しました (ptr={0}, size={1}, text={2})", wavPtr, size, text));
            }

            try
            {
                if (!string.IsNullOrEmpty(filename))
                {
                    // CreateWavData が返すポインタは完全なWAVファイル（RIFFヘッダー込み）
                    // ConvertToWavDataAquesTalk と違いヘッダーを剥がさず全バイトそのまま保存する
                    byte[] wavData = new byte[size];
                    Marshal.Copy(wavPtr, wavData, 0, size);
                    File.WriteAllBytes(filename, wavData);
                    Console.WriteLine(string.Format("[speakraw] 保存完了: {0} ({1} bytes)", filename, wavData.Length));
                }
            }
            finally
            {
                // 必ずアンマネージドメモリを解放する
                voiceObj.FreeWavData(wavPtr);
            }
        }

        private void WaitForTaskCompletion()
        {
            const int timeout = 10000;
            int waited = 0;
            while (Pub.TalkTaskCount > 0 && waited < timeout)
            {
                Thread.Sleep(3);
                waited += 3;
            }

            if (Pub.TalkTaskCount > 0)
                throw new TimeoutException("Talk task did not complete within the expected time.");
        }

        #endregion

        #region ■設定クラス

        public class Settings_HTTPAPICreateWav : SettingsBase
        {
            public int Port { get; set; } = 50088;

            internal Plugin_HTTPAPICreateWav Plugin;

            public Settings_HTTPAPICreateWav() { }

            public Settings_HTTPAPICreateWav(Plugin_HTTPAPICreateWav plugin)
            {
                Plugin = plugin;
            }

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
                [Description("使用するポートを変更します")]
                public int Port { get { return _Setting.Port; } set { _Setting.Port = value; } }
            }
        }

        #endregion
    }
}