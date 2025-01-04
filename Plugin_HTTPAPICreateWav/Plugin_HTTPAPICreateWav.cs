//プラグインのファイル名は、「Plugin_*.dll」という形式にして下さい。
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Drawing;
using System.Threading;
using System.ComponentModel;
using System.Windows.Forms;
using FNF.Utility;
using FNF.Controls;
using FNF.XmlSerializerSetting;
using FNF.BouyomiChanApp;
using System.Net;
using System.Security.Cryptography;
using System.Collections.Specialized;
using FNF.JsonParser;

namespace Plugin_HTTPAPICreateWav {
    public class Plugin_HTTPAPICreateWav : IPlugin {
        #region ■フィールド

        private Settings_HTTPAPICreateWav _Settings;                                                       //設定
        private SettingFormData_HTTPAPICreateWav _SettingFormData;
        private string                 _SettingFile = Base.CallAsmPath + Base.CallAsmName + ".setting"; //設定ファイルの保存場所
        private ToolStripButton        _Button;
        private ToolStripSeparator     _Separator;
        // HTTPリスナー
        private HttpListener _Server;
        #endregion


        #region ■IPluginメンバの実装

        public string           Name            { get { return "HTTP wave保存"; } }

        public string           Version         { get { return "1.0.0"; } }

        public string           Caption         { get { return "HTTPを使っていい感じにwavファイルを作って保存します。"; } } 

        public ISettingFormData SettingFormData { get { return _SettingFormData; } } //プラグインの設定画面情報（設定画面が必要なければnullを返してください）

        //プラグイン開始時処理
        public void Begin() {
            //設定ファイル読み込み
            _Settings = new Settings_HTTPAPICreateWav(this);
            _Settings.Load(_SettingFile);
            _SettingFormData = new SettingFormData_HTTPAPICreateWav(_Settings);


            //画面にボタンとセパレータを追加
            //ToDo ここで鯖を開くかのボタンにする
            //_Separator = new ToolStripSeparator();
            //Pub.ToolStrip.Items.Add(_Separator);
            //_Button = new ToolStripButton(Properties.Resources.ImgZihou);
            //_Button.ToolTipText = "現在時刻を読み上げ。";
            //_Button.Click      += Button_Click;
            //Pub.ToolStrip.Items.Add(_Button);
            // HTTPリスナーの初期化と開始
            _Server = new HttpListener();
            if (_Settings.Port > 0)
                this._Server.Prefixes.Add(string.Format("http://{0}:{1}/", (object)"localhost", (object)_Settings.Port));
            _Server.Start();
            _Server.BeginGetContext(new AsyncCallback(this.HttpListenerCallback), _Server);
        }

        //プラグイン終了時処理
        public void End() {
            //設定ファイル保存
            _Settings.Save(_SettingFile);

            if (_Server != null)
            {
                _Server.Stop();
                _Server.Close();
                _Server = null;
            }

            //画面からボタンとセパレータを削除
            //if (_Separator != null) {
            //    Pub.ToolStrip.Items.Remove(_Separator);
            //    _Separator.Dispose();
            //    _Separator = null;
            //}
            //if (_Button != null) {
            //    Pub.ToolStrip.Items.Remove(_Button);
            //    _Button.Dispose();
            //    _Button = null;
            //}
        }

        #endregion


        #region ■メソッド・イベント処理

        //ボタンが押されたら現在時刻を読み上げる
        //private void Button_Click(object sender, EventArgs e) {
        //    //AddTimeTalk(DateTime.Now, false);
        //}


        // HTTPリクエストを処理するコールバックメソッド
        private void HttpListenerCallback(IAsyncResult result)
        {
            if (_Server == null || !_Server.IsListening) return;

            HttpListenerContext context = _Server.EndGetContext(result);
            _Server.BeginGetContext(new AsyncCallback(HttpListenerCallback), _Server);

            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;
            string path = request.Url.AbsolutePath;

            string responseString = "OK";
            try
            {
                if (path == "/speak")
                {
                    // /speak の場合の処理
                    string query = request.RawUrl;
                    NameValueCollection queryParams = HttpUtilityEx.ParseQuery(query, true);

                    string text = queryParams["text"];
                    string filename = queryParams["filename"];
                    int speed = int.Parse(queryParams["speed"] ?? "-1");
                    int volume = int.Parse(queryParams["volume"] ?? "-1");
                    int voice = int.Parse(queryParams["voice"] ?? "0");
                    int tone = int.Parse(queryParams["tone"] ?? "100");
                    responseString = $"text={text}, speed={speed}, volume={volume}, voice={voice}, tone={tone}";
                    AddTalkTask(text, speed, volume, (VoiceType)voice, tone, filename);
                }
                else
                {
                    // その他のパスの場合の処理
                    responseString = "Not Found";
                    response.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                responseString = $"Error: {ex.Message}";
            }

            response.ContentType = "text/plain";
            response.ContentEncoding = Encoding.UTF8;
            response.Headers.Add("Access-Control-Allow-Origin", "*");

            byte[] buffer = Encoding.UTF8.GetBytes(responseString);
            response.ContentLength64 = buffer.Length;
            response.OutputStream.Write(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        // 任意のテキストを読み上げる
        private void AddTalkTask(string text, int speed, int volume, VoiceType voice, int tone, string filename)
        {
            Pub.AddTalkTask(text, speed, tone, volume, voice, filename);

            // タイムアウトを設定（例: 10秒）
            int timeout = 10000;
            int waited = 0;

            while (Pub.TalkTaskCount > 0 && waited < timeout)
            {
                Thread.Sleep(5);
                waited += 5;
            }

            if (Pub.TalkTaskCount > 0)
            {
                // タイムアウト処理
                throw new TimeoutException("Talk task did not complete within the expected time.");
            }
        }

        #endregion


        #region ■クラス・構造体

        // 設定クラス（設定画面表示・ファイル保存を簡略化。publicなメンバだけ保存される。XmlSerializerで処理できるクラスのみ使用可。）
        public class Settings_HTTPAPICreateWav : SettingsBase {
            //保存される情報（設定画面からも参照される）
            public int Port { get; set; } = 50088;


            //作成元プラグイン
            internal Plugin_HTTPAPICreateWav Plugin;

            //コンストラクタ
            public Settings_HTTPAPICreateWav() {
            }

            //コンストラクタ
            public Settings_HTTPAPICreateWav(Plugin_HTTPAPICreateWav pZihou) {
                Plugin = pZihou;
            }

            //GUIなどから当オブジェクトの読み込み(設定セーブ時・設定画面表示時に呼ばれる)
            public override void ReadSettings() {
                
            }

            //当オブジェクトからGUIなどへの反映(設定ロード時・設定更新時に呼ばれる)
            public override void WriteSettings() {
                //Plugin.SetNextAlart();
            }
        }

        // 設定画面表示用クラス（設定画面表示・ファイル保存を簡略化。publicなメンバだけ保存される。XmlSerializerで処理できるクラスのみ使用可。）
        public class SettingFormData_HTTPAPICreateWav : ISettingFormData {
            Settings_HTTPAPICreateWav _Setting;

            public string       Title     { get { return _Setting.Plugin.Name; } }
            public bool         ExpandAll { get { return false; } }
            public SettingsBase Setting   { get { return _Setting; } }

            public SettingFormData_HTTPAPICreateWav(Settings_HTTPAPICreateWav setting) {
                _Setting = setting;
                PBase    = new SBase(_Setting);
            }

            //設定画面で表示されるクラス(ISettingPropertyGrid)
            public SBase PBase;
            public class SBase : ISettingPropertyGrid {
                Settings_HTTPAPICreateWav _Setting;
                public SBase(Settings_HTTPAPICreateWav setting) { _Setting = setting; }
                public string GetName() { return "HTTP設定"; }

                [Category   ("基本設定")]
                [DisplayName("ポートを変更する")]
                [Description("使用するポートを変更します")]
                public int Port { get { return _Setting.Port; } set { _Setting.Port = value; } }

                /* ISettingPropertyGridでは設定画面での表示項目を指定できます。
                [Category   ("分類")]
                [DisplayName("表示名")]
                [Description("説明文")]
                [DefaultValue(0)]        //デフォルト値：強調表示されないだけ
                [Browsable(false)]       //PropertyGridで表示しない
                [ReadOnly(true)]         //PropertyGridで読み込み専用にする
                string  ファイル選択     →[Editor(typeof(System.Windows.Forms.Design.FolderNameEditor),       typeof(System.Drawing.Design.UITypeEditor))]
                string  フォルダ選択     →[Editor(typeof(System.Windows.Forms.Design.FileNameEditor),         typeof(System.Drawing.Design.UITypeEditor))]
                string  複数行文字列入力 →[Editor(typeof(System.ComponentModel.Design.MultilineStringEditor), typeof(System.Drawing.Design.UITypeEditor))]
                */
            }
        }

        #endregion
    }
}
