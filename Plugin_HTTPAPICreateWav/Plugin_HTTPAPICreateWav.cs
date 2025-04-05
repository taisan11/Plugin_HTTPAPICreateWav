using System;
using System.Text;
using System.Threading;
using System.ComponentModel;
using System.Windows.Forms;
using FNF.Utility;
using FNF.XmlSerializerSetting;
using FNF.BouyomiChanApp;
using System.Net;
using System.Collections.Specialized;
using HarmonyLib;
using System.Reflection;
using System.Collections.Generic;

namespace Plugin_HTTPAPICreateWav {

    // FNF.Utilityの拡張クラスを定義
    public static class BouyomiChanExtensions {
        // 拡張メソッドでAddTalkTaskActionを追加
        private static CustomWeakTable<FNF.Utility.BouyomiChan, Action<object>> _actions =
            new CustomWeakTable<FNF.Utility.BouyomiChan, Action<object>>();

        public static void SetAddTalkTaskAction(this FNF.Utility.BouyomiChan bouyomiChan, Action<object> action)
        {
            _actions.Add(bouyomiChan, action);
        }

        public static Action<object> GetAddTalkTaskAction(this FNF.Utility.BouyomiChan bouyomiChan)
        {
            Action<object> action;
            _actions.TryGetValue(bouyomiChan, out action);
            return action;
        }
    }

    public static class BouyomiChanPatches {
        [HarmonyPatch(typeof(FNF.Utility.BouyomiChan), "AddTalkTask")]
        [HarmonyPostfix]
        public static void AddTalkTaskPostfix(FNF.Utility.BouyomiChan __instance, object args)
        {
            // TalkTaskEventArgsの代わりにobjectを使用
            Console.WriteLine("発話タスクが追加されました");
        }

        [HarmonyPatch(typeof(FNF.Utility.BouyomiChan), MethodType.Constructor)]
        [HarmonyPostfix]
        public static void ConstructorPostfix(FNF.Utility.BouyomiChan __instance)
        {
            try
            {
                // privateメソッドへアクセスするためのデリゲートを作成
                var addTalkTaskMethod = typeof(FNF.Utility.BouyomiChan)
                    .GetMethod("AddTalkTask", BindingFlags.NonPublic | BindingFlags.Instance);

                if (addTalkTaskMethod != null)
                {
                    // objを引数にとるActionを生成（TalkTaskEventArgsではなくobjectを使用）
                    Action<object> addTalkTaskDelegate = (obj) => {
                        addTalkTaskMethod.Invoke(__instance, new object[] { obj });
                    };

                    // 拡張メソッドを使用してActionを保存
                    __instance.SetAddTalkTaskAction(addTalkTaskDelegate);
                    Console.WriteLine("AddTalkTaskActionが正常に登録されました");
                }
                else
                {
                    Console.WriteLine("AddTalkTaskメソッドが見つかりませんでした");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ConstructorPostfix中にエラーが発生: {ex.Message}");
            }
        }
    }

    // カスタムWeakTableクラスの定義（.NET 3.5では標準で使用できないため）
    public class CustomWeakTable<TKey, TValue> where TKey : class where TValue : class {
        private Dictionary<WeakReference, TValue> _table = new Dictionary<WeakReference, TValue>(new WeakReferenceComparer());

        public void Add(TKey key, TValue value)
        {
            CleanupDeadReferences();
            _table.Add(new WeakReference(key), value);
        }

        public bool TryGetValue(TKey key, out TValue value)
        {
            CleanupDeadReferences();
            foreach (var pair in _table)
            {
                if (pair.Key.Target == key)
                {
                    value = pair.Value;
                    return true;
                }
            }
            value = null;
            return false;
        }

        private void CleanupDeadReferences()
        {
            // LINQ Whereを使わずに直接ループで処理
            List<WeakReference> deadKeys = new List<WeakReference>();
            foreach (WeakReference wr in _table.Keys)
            {
                if (!wr.IsAlive)
                {
                    deadKeys.Add(wr);
                }
            }

            foreach (WeakReference key in deadKeys)
            {
                _table.Remove(key);
            }
        }

        private class WeakReferenceComparer : IEqualityComparer<WeakReference> {
            public bool Equals(WeakReference x, WeakReference y)
            {
                return ReferenceEquals(x.Target, y.Target);
            }

            public int GetHashCode(WeakReference obj)
            {
                return obj.Target != null ? obj.Target.GetHashCode() : 0;
            }
        }
    }

    public static class HarmonyHelper {
        private static Harmony harmony;

        public static void PatchAll()
        {
            if (harmony == null)
            {
                harmony = new Harmony("My.Harmony.Patch");
                harmony.PatchAll();
                Console.WriteLine("Harmony パッチを適用しました。");
            }
            else
            {
                Console.WriteLine("Harmony はすでに適用されています。");
            }
        }

        public static void UnpatchAll()
        {
            if (harmony != null)
            {
                harmony.UnpatchAll("My.Harmony.Patch");
                harmony = null;
                Console.WriteLine("Harmony パッチを削除しました。");
            }
            else
            {
                Console.WriteLine("Harmony は適用されていません。");
            }
        }
    }

    public class Plugin_HTTPAPICreateWav : IPlugin {
        #region ■フィールド

        private Settings_HTTPAPICreateWav _Settings;                                                       //設定
        private SettingFormData_HTTPAPICreateWav _SettingFormData;
        private string _SettingFile = Base.CallAsmPath + Base.CallAsmName + ".setting"; //設定ファイルの保存場所
        // HTTPリスナー
        private HttpListener _Server;
        #endregion

        #region ■IPluginメンバの実装

        public string Name { get { return "HTTP wave保存"; } }

        public string Version { get { return "1.0.0"; } }

        public string Caption { get { return "HTTPを使っていい感じにwavファイルを作って保存します。"; } }

        public ISettingFormData SettingFormData { get { return _SettingFormData; } } //プラグインの設定画面情報（設定画面が必要なければnullを返してください）

        //プラグイン開始時処理
        public void Begin()
        {
            //設定ファイル読み込み
            _Settings = new Settings_HTTPAPICreateWav(this);
            _Settings.Load(_SettingFile);
            _SettingFormData = new SettingFormData_HTTPAPICreateWav(_Settings);

            // BouyomiChanのインスタンスを確認
            if (Pub.FormMain.BC == null)
            {
                Console.WriteLine("BouyomiChanのインスタンスが存在しません。");
                return;
            }

            // Harmonyパッチを適用
            HarmonyHelper.PatchAll();

            // パッチ適用の確認
            if (Pub.FormMain.BC.GetAddTalkTaskAction() != null)
            {
                Console.WriteLine("パッチが正常に適用されました。");
            }
            else
            {
                Console.WriteLine("パッチの適用に失敗しました。");
            }

            // HTTPリスナーの初期化と開始
            _Server = new HttpListener();
            if (_Settings.Port > 0)
                _Server.Prefixes.Add(string.Format("http://{0}:{1}/", "localhost", _Settings.Port));
            _Server.Start();
            _Server.BeginGetContext(new AsyncCallback(this.HttpListenerCallback), _Server);
        }

        //プラグイン終了時処理
        public void End()
        {
            //設定ファイル保存
            _Settings.Save(_SettingFile);

            if (_Server != null)
            {
                _Server.Stop();
                _Server.Close();
                _Server = null;
            }

            // Harmonyパッチの除去
            HarmonyHelper.UnpatchAll();
        }

        #endregion

        #region ■メソッド・イベント処理

        // HTTPリクエストを処理するコールバックメソッド
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
                return; // HTTPリスナーが閉じられている場合
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
                    responseString = string.Format("text={0}, speed={1}, volume={2}, voice={3}, tone={4}",
                                                   text, speed, volume, voice, tone);
                    AddTalkTask(text, speed, volume, (FNF.Utility.VoiceType)voice, tone, filename);
                }
                else if (path == "/speakraw")
                {
                    // /speakraw の場合の処理
                    string query = request.RawUrl;
                    NameValueCollection queryParams = HttpUtilityEx.ParseQuery(query, true);

                    string text = queryParams["text"];
                    string filename = queryParams["filename"];
                    int speed = int.Parse(queryParams["speed"] ?? "-1");
                    int volume = int.Parse(queryParams["volume"] ?? "-1");
                    int voice = int.Parse(queryParams["voice"] ?? "0");
                    int tone = int.Parse(queryParams["tone"] ?? "100");
                    responseString = string.Format("text={0}, speed={1}, volume={2}, voice={3}, tone={4}",
                                                   text, speed, volume, voice, tone);
                    AddTalkRawTask(text, speed, volume, (FNF.Utility.VoiceType)voice, tone, filename);
                }
                else
                {
                    // その他のパスの場合の処理
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

        // 任意のテキストを読み上げる
        private void AddTalkTask(string text, int speed, int volume, FNF.Utility.VoiceType voice, int tone, string filename)
        {
            Pub.AddTalkTask(text, speed, tone, volume, voice, filename);

            // タイムアウトを設定（例: 10秒）
            int timeout = 10000;
            int waited = 0;

            while (Pub.TalkTaskCount > 0 && waited < timeout)
            {
                Thread.Sleep(3);
                waited += 3;
            }

            if (Pub.TalkTaskCount > 0)
            {
                // タイムアウト処理
                throw new TimeoutException("Talk task did not complete within the expected time.");
            }
        }
        //生
        private void AddTalkRawTask(string text, int speed, int volume, FNF.Utility.VoiceType voice, int tone, string filename)
        {
            // GetAddTalkTaskAction()はAction<object>を返すので、適切なパラメータオブジェクトを作成する必要がある

            // TalkTaskEventArgsのインスタンスを作成するか、または適切なオブジェクトを作成
            try
            {
                // リフレクションでTalkTaskEventArgsを作成
                Type talkTaskEventArgsType = typeof(FNF.Utility.BouyomiChan).Assembly
                    .GetType("FNF.Utility.TalkTaskEventArgs");

                if (talkTaskEventArgsType != null)
                {
                    // コンストラクタを取得（必要な引数を確認する必要があります）
                    ConstructorInfo constructor = talkTaskEventArgsType.GetConstructor(
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(string), typeof(FNF.Utility.VoiceType), typeof(int), typeof(int), typeof(int), typeof(string) },
                        null);

                    if (constructor != null)
                    {
                        // TalkTaskEventArgsのインスタンスを作成
                        object taskEventArgs = constructor.Invoke(
                            new object[] { text, voice, tone, volume, speed, filename });

                        // Actionを実行
                        Pub.FormMain.BC.GetAddTalkTaskAction().Invoke(taskEventArgs);

                        // タイムアウトを設定（例: 10秒）
                        int timeout = 10000;
                        int waited = 0;

                        while (Pub.TalkTaskCount > 0 && waited < timeout)
                        {
                            Thread.Sleep(3);
                            waited += 3;
                        }

                        if (Pub.TalkTaskCount > 0)
                        {
                            // タイムアウト処理
                            throw new TimeoutException("Talk task did not complete within the expected time.");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("TalkTaskEventArgsの適切なコンストラクタが見つかりませんでした");
                    }
                }
                else
                {
                    throw new InvalidOperationException("TalkTaskEventArgs型が見つかりませんでした");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("AddTalkRawTaskでエラーが発生しました: " + ex.Message);
                // 通常のパブリックAPIを使用してフォールバック
                Pub.AddTalkTask(text, speed, tone, volume, voice, filename);
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
            public Settings_HTTPAPICreateWav()
            {
            }

            //コンストラクタ
            public Settings_HTTPAPICreateWav(Plugin_HTTPAPICreateWav plugin)
            {
                Plugin = plugin;
            }

            //GUIなどから当オブジェクトの読み込み(設定セーブ時・設定画面表示時に呼ばれる)
            public override void ReadSettings()
            {

            }

            //当オブジェクトからGUIなどへの反映(設定ロード時・設定更新時に呼ばれる)
            public override void WriteSettings()
            {
            }
        }

        // 設定画面表示用クラス（設定画面表示・ファイル保存を簡略化。publicなメンバだけ保存される。XmlSerializerで処理できるクラスのみ使用可。）
        public class SettingFormData_HTTPAPICreateWav : ISettingFormData {
            Settings_HTTPAPICreateWav _Setting;

            public string Title { get { return _Setting.Plugin.Name; } }
            public bool ExpandAll { get { return false; } }
            public SettingsBase Setting { get { return _Setting; } }

            public SettingFormData_HTTPAPICreateWav(Settings_HTTPAPICreateWav setting)
            {
                _Setting = setting;
                PBase = new SBase(_Setting);
            }

            //設定画面で表示されるクラス(ISettingPropertyGrid)
            public SBase PBase;
            public class SBase : ISettingPropertyGrid {
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
