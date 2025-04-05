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

    // FNF.Utility�̊g���N���X���`
    public static class BouyomiChanExtensions {
        // �g�����\�b�h��AddTalkTaskAction��ǉ�
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
            // TalkTaskEventArgs�̑����object���g�p
            Console.WriteLine("���b�^�X�N���ǉ�����܂���");
        }

        [HarmonyPatch(typeof(FNF.Utility.BouyomiChan), MethodType.Constructor)]
        [HarmonyPostfix]
        public static void ConstructorPostfix(FNF.Utility.BouyomiChan __instance)
        {
            try
            {
                // private���\�b�h�փA�N�Z�X���邽�߂̃f���Q�[�g���쐬
                var addTalkTaskMethod = typeof(FNF.Utility.BouyomiChan)
                    .GetMethod("AddTalkTask", BindingFlags.NonPublic | BindingFlags.Instance);

                if (addTalkTaskMethod != null)
                {
                    // obj�������ɂƂ�Action�𐶐��iTalkTaskEventArgs�ł͂Ȃ�object���g�p�j
                    Action<object> addTalkTaskDelegate = (obj) => {
                        addTalkTaskMethod.Invoke(__instance, new object[] { obj });
                    };

                    // �g�����\�b�h���g�p����Action��ۑ�
                    __instance.SetAddTalkTaskAction(addTalkTaskDelegate);
                    Console.WriteLine("AddTalkTaskAction������ɓo�^����܂���");
                }
                else
                {
                    Console.WriteLine("AddTalkTask���\�b�h��������܂���ł���");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ConstructorPostfix���ɃG���[������: {ex.Message}");
            }
        }
    }

    // �J�X�^��WeakTable�N���X�̒�`�i.NET 3.5�ł͕W���Ŏg�p�ł��Ȃ����߁j
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
            // LINQ Where���g�킸�ɒ��ڃ��[�v�ŏ���
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
                Console.WriteLine("Harmony �p�b�`��K�p���܂����B");
            }
            else
            {
                Console.WriteLine("Harmony �͂��łɓK�p����Ă��܂��B");
            }
        }

        public static void UnpatchAll()
        {
            if (harmony != null)
            {
                harmony.UnpatchAll("My.Harmony.Patch");
                harmony = null;
                Console.WriteLine("Harmony �p�b�`���폜���܂����B");
            }
            else
            {
                Console.WriteLine("Harmony �͓K�p����Ă��܂���B");
            }
        }
    }

    public class Plugin_HTTPAPICreateWav : IPlugin {
        #region ���t�B�[���h

        private Settings_HTTPAPICreateWav _Settings;                                                       //�ݒ�
        private SettingFormData_HTTPAPICreateWav _SettingFormData;
        private string _SettingFile = Base.CallAsmPath + Base.CallAsmName + ".setting"; //�ݒ�t�@�C���̕ۑ��ꏊ
        // HTTP���X�i�[
        private HttpListener _Server;
        #endregion

        #region ��IPlugin�����o�̎���

        public string Name { get { return "HTTP wave�ۑ�"; } }

        public string Version { get { return "1.0.0"; } }

        public string Caption { get { return "HTTP���g���Ă���������wav�t�@�C��������ĕۑ����܂��B"; } }

        public ISettingFormData SettingFormData { get { return _SettingFormData; } } //�v���O�C���̐ݒ��ʏ��i�ݒ��ʂ��K�v�Ȃ����null��Ԃ��Ă��������j

        //�v���O�C���J�n������
        public void Begin()
        {
            //�ݒ�t�@�C���ǂݍ���
            _Settings = new Settings_HTTPAPICreateWav(this);
            _Settings.Load(_SettingFile);
            _SettingFormData = new SettingFormData_HTTPAPICreateWav(_Settings);

            // BouyomiChan�̃C���X�^���X���m�F
            if (Pub.FormMain.BC == null)
            {
                Console.WriteLine("BouyomiChan�̃C���X�^���X�����݂��܂���B");
                return;
            }

            // Harmony�p�b�`��K�p
            HarmonyHelper.PatchAll();

            // �p�b�`�K�p�̊m�F
            if (Pub.FormMain.BC.GetAddTalkTaskAction() != null)
            {
                Console.WriteLine("�p�b�`������ɓK�p����܂����B");
            }
            else
            {
                Console.WriteLine("�p�b�`�̓K�p�Ɏ��s���܂����B");
            }

            // HTTP���X�i�[�̏������ƊJ�n
            _Server = new HttpListener();
            if (_Settings.Port > 0)
                _Server.Prefixes.Add(string.Format("http://{0}:{1}/", "localhost", _Settings.Port));
            _Server.Start();
            _Server.BeginGetContext(new AsyncCallback(this.HttpListenerCallback), _Server);
        }

        //�v���O�C���I��������
        public void End()
        {
            //�ݒ�t�@�C���ۑ�
            _Settings.Save(_SettingFile);

            if (_Server != null)
            {
                _Server.Stop();
                _Server.Close();
                _Server = null;
            }

            // Harmony�p�b�`�̏���
            HarmonyHelper.UnpatchAll();
        }

        #endregion

        #region �����\�b�h�E�C�x���g����

        // HTTP���N�G�X�g����������R�[���o�b�N���\�b�h
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
                return; // HTTP���X�i�[�������Ă���ꍇ
            }
            catch (Exception ex)
            {
                Console.WriteLine("HTTP�R���e�L�X�g�擾���ɃG���[: " + ex.Message);
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
                    // /speak �̏ꍇ�̏���
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
                    // /speakraw �̏ꍇ�̏���
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
                    // ���̑��̃p�X�̏ꍇ�̏���
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
                Console.WriteLine("HTTP���X�|���X�������ɃG���[: " + ex.Message);
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

        // �C�ӂ̃e�L�X�g��ǂݏグ��
        private void AddTalkTask(string text, int speed, int volume, FNF.Utility.VoiceType voice, int tone, string filename)
        {
            Pub.AddTalkTask(text, speed, tone, volume, voice, filename);

            // �^�C���A�E�g��ݒ�i��: 10�b�j
            int timeout = 10000;
            int waited = 0;

            while (Pub.TalkTaskCount > 0 && waited < timeout)
            {
                Thread.Sleep(3);
                waited += 3;
            }

            if (Pub.TalkTaskCount > 0)
            {
                // �^�C���A�E�g����
                throw new TimeoutException("Talk task did not complete within the expected time.");
            }
        }
        //��
        private void AddTalkRawTask(string text, int speed, int volume, FNF.Utility.VoiceType voice, int tone, string filename)
        {
            // GetAddTalkTaskAction()��Action<object>��Ԃ��̂ŁA�K�؂ȃp�����[�^�I�u�W�F�N�g���쐬����K�v������

            // TalkTaskEventArgs�̃C���X�^���X���쐬���邩�A�܂��͓K�؂ȃI�u�W�F�N�g���쐬
            try
            {
                // ���t���N�V������TalkTaskEventArgs���쐬
                Type talkTaskEventArgsType = typeof(FNF.Utility.BouyomiChan).Assembly
                    .GetType("FNF.Utility.TalkTaskEventArgs");

                if (talkTaskEventArgsType != null)
                {
                    // �R���X�g���N�^���擾�i�K�v�Ȉ������m�F����K�v������܂��j
                    ConstructorInfo constructor = talkTaskEventArgsType.GetConstructor(
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(string), typeof(FNF.Utility.VoiceType), typeof(int), typeof(int), typeof(int), typeof(string) },
                        null);

                    if (constructor != null)
                    {
                        // TalkTaskEventArgs�̃C���X�^���X���쐬
                        object taskEventArgs = constructor.Invoke(
                            new object[] { text, voice, tone, volume, speed, filename });

                        // Action�����s
                        Pub.FormMain.BC.GetAddTalkTaskAction().Invoke(taskEventArgs);

                        // �^�C���A�E�g��ݒ�i��: 10�b�j
                        int timeout = 10000;
                        int waited = 0;

                        while (Pub.TalkTaskCount > 0 && waited < timeout)
                        {
                            Thread.Sleep(3);
                            waited += 3;
                        }

                        if (Pub.TalkTaskCount > 0)
                        {
                            // �^�C���A�E�g����
                            throw new TimeoutException("Talk task did not complete within the expected time.");
                        }
                    }
                    else
                    {
                        throw new InvalidOperationException("TalkTaskEventArgs�̓K�؂ȃR���X�g���N�^��������܂���ł���");
                    }
                }
                else
                {
                    throw new InvalidOperationException("TalkTaskEventArgs�^��������܂���ł���");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("AddTalkRawTask�ŃG���[���������܂���: " + ex.Message);
                // �ʏ�̃p�u���b�NAPI���g�p���ăt�H�[���o�b�N
                Pub.AddTalkTask(text, speed, tone, volume, voice, filename);
            }
        }

        #endregion

        #region ���N���X�E�\����

        // �ݒ�N���X�i�ݒ��ʕ\���E�t�@�C���ۑ����ȗ����Bpublic�ȃ����o�����ۑ������BXmlSerializer�ŏ����ł���N���X�̂ݎg�p�B�j
        public class Settings_HTTPAPICreateWav : SettingsBase {
            //�ۑ��������i�ݒ��ʂ�����Q�Ƃ����j
            public int Port { get; set; } = 50088;

            //�쐬���v���O�C��
            internal Plugin_HTTPAPICreateWav Plugin;

            //�R���X�g���N�^
            public Settings_HTTPAPICreateWav()
            {
            }

            //�R���X�g���N�^
            public Settings_HTTPAPICreateWav(Plugin_HTTPAPICreateWav plugin)
            {
                Plugin = plugin;
            }

            //GUI�Ȃǂ��瓖�I�u�W�F�N�g�̓ǂݍ���(�ݒ�Z�[�u���E�ݒ��ʕ\�����ɌĂ΂��)
            public override void ReadSettings()
            {

            }

            //���I�u�W�F�N�g����GUI�Ȃǂւ̔��f(�ݒ胍�[�h���E�ݒ�X�V���ɌĂ΂��)
            public override void WriteSettings()
            {
            }
        }

        // �ݒ��ʕ\���p�N���X�i�ݒ��ʕ\���E�t�@�C���ۑ����ȗ����Bpublic�ȃ����o�����ۑ������BXmlSerializer�ŏ����ł���N���X�̂ݎg�p�B�j
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

            //�ݒ��ʂŕ\�������N���X(ISettingPropertyGrid)
            public SBase PBase;
            public class SBase : ISettingPropertyGrid {
                Settings_HTTPAPICreateWav _Setting;
                public SBase(Settings_HTTPAPICreateWav setting) { _Setting = setting; }
                public string GetName() { return "HTTP�ݒ�"; }

                [Category("��{�ݒ�")]
                [DisplayName("�|�[�g��ύX����")]
                [Description("�g�p����|�[�g��ύX���܂�")]
                public int Port { get { return _Setting.Port; } set { _Setting.Port = value; } }
            }
        }

        #endregion
    }
}
