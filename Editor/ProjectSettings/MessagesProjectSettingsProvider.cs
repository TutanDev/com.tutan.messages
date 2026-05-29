using System.Collections.Generic;
using Tutan.Messages.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace TUTAN.Messages.Editor
{
    public class MessagesProjectSettingsProvider : SettingsProvider
    {
        const string KeyAuthoringFoldout = "Tutan.Messages.Settings.AuthoringFoldout";

        private SerializedObject _Settings;

        public MessagesProjectSettingsProvider(string path, SettingsScope scopes, IEnumerable<string> keywords = null)
            : base(path, scopes, keywords)
        {
            label = "Messages";
        }

        [SettingsProvider]
        public static SettingsProvider CreateMessagesProjectSettingsProvider()
        {
            var provider = new MessagesProjectSettingsProvider("Project/Tutan/Messages", SettingsScope.Project);
            provider.keywords = GetSearchKeywordsFromSerializedObject(new SerializedObject(MessagesProjectSettings.instance));
            return provider;
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            _Settings = new SerializedObject(MessagesProjectSettings.instance);

            var container = new VisualElement();
            container.style.paddingLeft = 10;
            container.style.paddingRight = 10;
            container.style.paddingTop = 10;
            container.style.flexGrow = 1;
            rootElement.Add(container);

            var title = new Label("Messages");
            title.style.fontSize = 19;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            container.Add(title);

            AddPropertyField(container, nameof(MessagesProjectSettings.AutoInstallDrainers));
            AddPropertyField(container, nameof(MessagesProjectSettings.AutoInstallCommandBus));
            AddPropertyField(container, nameof(MessagesProjectSettings.EnableInstrumentation));

            var foldout = new Foldout
            {
                text = "Commands Authoring",
                value = EditorPrefs.GetBool(KeyAuthoringFoldout, false),
            };
            foldout.style.marginTop = 12;
            foldout.RegisterValueChangedCallback(e => EditorPrefs.SetBool(KeyAuthoringFoldout, e.newValue));

            var view = new CommandsAuthoringView();
            view.style.minHeight = 360;
            foldout.Add(view);
            container.Add(foldout);

            container.Bind(_Settings);
            container.TrackSerializedObjectValue(_Settings, _ =>
            {
                MessagesProjectSettings.instance.Save();
                MessagesProjectSettings.instance.ApplySideEffects();
            });
        }

        public override void OnDeactivate()
        {
            _Settings = null;
        }

        void AddPropertyField(VisualElement parent, string propertyName)
        {
            var prop = _Settings.FindProperty(propertyName);
            if (prop != null) parent.Add(new PropertyField(prop));
        }
    }
}
