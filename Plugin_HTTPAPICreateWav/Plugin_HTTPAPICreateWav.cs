//�v���O�C���̃t�@�C�����́A�uPlugin_*.dll�v�Ƃ����`���ɂ��ĉ������B
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
        #region ���t�B�[���h

        private Settings_HTTPAPICreateWav _Settings;                                                       //�ݒ�
        private SettingFormData_HTTPAPICreateWav _SettingFormData;
        private string                 _SettingFile = Base.CallAsmPath + Base.CallAsmName + ".setting"; //�ݒ�t�@�C���̕ۑ��ꏊ
        private ToolStripButton        _Button;
        private ToolStripSeparator     _Separator;
        // HTTP���X�i�[
        private HttpListener _Server;
        #endregion


        #region ��IPlugin�����o�̎���

        public string           Name            { get { return "HTTP wave�ۑ�"; } }

        public string           Version         { get { return "1.0.0"; } }

        public string           Caption         { get { return "HTTP���g���Ă���������wav�t�@�C��������ĕۑ����܂��B"; } } 

        public ISettingFormData SettingFormData { get { return _SettingFormData; } } //�v���O�C���̐ݒ��ʏ��i�ݒ��ʂ��K�v�Ȃ����null��Ԃ��Ă��������j

        //�v���O�C���J�n������
        public void Begin() {
            //�ݒ�t�@�C���ǂݍ���
            _Settings = new Settings_HTTPAPICreateWav(this);
            _Settings.Load(_SettingFile);
            _SettingFormData = new SettingFormData_HTTPAPICreateWav(_Settings);


            //��ʂɃ{�^���ƃZ�p���[�^��ǉ�
            //ToDo �����ŎI���J�����̃{�^���ɂ���
            //_Separator = new ToolStripSeparator();
            //Pub.ToolStrip.Items.Add(_Separator);
            //_Button = new ToolStripButton(Properties.Resources.ImgZihou);
            //_Button.ToolTipText = "���ݎ�����ǂݏグ�B";
            //_Button.Click      += Button_Click;
            //Pub.ToolStrip.Items.Add(_Button);
            // HTTP���X�i�[�̏������ƊJ�n
            _Server = new HttpListener();
            if (_Settings.Port > 0)
                this._Server.Prefixes.Add(string.Format("http://{0}:{1}/", (object)"localhost", (object)_Settings.Port));
            _Server.Start();
            _Server.BeginGetContext(new AsyncCallback(this.HttpListenerCallback), _Server);
        }

        //�v���O�C���I��������
        public void End() {
            //�ݒ�t�@�C���ۑ�
            _Settings.Save(_SettingFile);

            if (_Server != null)
            {
                _Server.Stop();
                _Server.Close();
                _Server = null;
            }

            //��ʂ���{�^���ƃZ�p���[�^���폜
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


        #region �����\�b�h�E�C�x���g����

        //�{�^���������ꂽ�猻�ݎ�����ǂݏグ��
        //private void Button_Click(object sender, EventArgs e) {
        //    //AddTimeTalk(DateTime.Now, false);
        //}


        // HTTP���N�G�X�g����������R�[���o�b�N���\�b�h
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
                    // /speak �̏ꍇ�̏���
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
                    // ���̑��̃p�X�̏ꍇ�̏���
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

        // �C�ӂ̃e�L�X�g��ǂݏグ��
        private void AddTalkTask(string text, int speed, int volume, VoiceType voice, int tone, string filename)
        {
            Pub.AddTalkTask(text, speed, tone, volume, voice, filename);

            // �^�C���A�E�g��ݒ�i��: 10�b�j
            int timeout = 10000;
            int waited = 0;

            while (Pub.TalkTaskCount > 0 && waited < timeout)
            {
                Thread.Sleep(5);
                waited += 5;
            }

            if (Pub.TalkTaskCount > 0)
            {
                // �^�C���A�E�g����
                throw new TimeoutException("Talk task did not complete within the expected time.");
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
            public Settings_HTTPAPICreateWav() {
            }

            //�R���X�g���N�^
            public Settings_HTTPAPICreateWav(Plugin_HTTPAPICreateWav pZihou) {
                Plugin = pZihou;
            }

            //GUI�Ȃǂ��瓖�I�u�W�F�N�g�̓ǂݍ���(�ݒ�Z�[�u���E�ݒ��ʕ\�����ɌĂ΂��)
            public override void ReadSettings() {
                
            }

            //���I�u�W�F�N�g����GUI�Ȃǂւ̔��f(�ݒ胍�[�h���E�ݒ�X�V���ɌĂ΂��)
            public override void WriteSettings() {
                //Plugin.SetNextAlart();
            }
        }

        // �ݒ��ʕ\���p�N���X�i�ݒ��ʕ\���E�t�@�C���ۑ����ȗ����Bpublic�ȃ����o�����ۑ������BXmlSerializer�ŏ����ł���N���X�̂ݎg�p�B�j
        public class SettingFormData_HTTPAPICreateWav : ISettingFormData {
            Settings_HTTPAPICreateWav _Setting;

            public string       Title     { get { return _Setting.Plugin.Name; } }
            public bool         ExpandAll { get { return false; } }
            public SettingsBase Setting   { get { return _Setting; } }

            public SettingFormData_HTTPAPICreateWav(Settings_HTTPAPICreateWav setting) {
                _Setting = setting;
                PBase    = new SBase(_Setting);
            }

            //�ݒ��ʂŕ\�������N���X(ISettingPropertyGrid)
            public SBase PBase;
            public class SBase : ISettingPropertyGrid {
                Settings_HTTPAPICreateWav _Setting;
                public SBase(Settings_HTTPAPICreateWav setting) { _Setting = setting; }
                public string GetName() { return "HTTP�ݒ�"; }

                [Category   ("��{�ݒ�")]
                [DisplayName("�|�[�g��ύX����")]
                [Description("�g�p����|�[�g��ύX���܂�")]
                public int Port { get { return _Setting.Port; } set { _Setting.Port = value; } }

                /* ISettingPropertyGrid�ł͐ݒ��ʂł̕\�����ڂ��w��ł��܂��B
                [Category   ("����")]
                [DisplayName("�\����")]
                [Description("������")]
                [DefaultValue(0)]        //�f�t�H���g�l�F�����\������Ȃ�����
                [Browsable(false)]       //PropertyGrid�ŕ\�����Ȃ�
                [ReadOnly(true)]         //PropertyGrid�œǂݍ��ݐ�p�ɂ���
                string  �t�@�C���I��     ��[Editor(typeof(System.Windows.Forms.Design.FolderNameEditor),       typeof(System.Drawing.Design.UITypeEditor))]
                string  �t�H���_�I��     ��[Editor(typeof(System.Windows.Forms.Design.FileNameEditor),         typeof(System.Drawing.Design.UITypeEditor))]
                string  �����s��������� ��[Editor(typeof(System.ComponentModel.Design.MultilineStringEditor), typeof(System.Drawing.Design.UITypeEditor))]
                */
            }
        }

        #endregion
    }
}
