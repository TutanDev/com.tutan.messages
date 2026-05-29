using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace TUTAN.Messages.Editor
{
    [FilePath(relativePath: _FilePath, FilePathAttribute.Location.ProjectFolder)]
    public class MessagesProjectSettings : ScriptableSingleton<MessagesProjectSettings>
    {
        internal const string _PackageName = "com.tutan.messages";
        internal const string _AssetName = "MessagesProjectSettings.asset";
        internal const string _FilePath = "ProjectSettings/Packages/" + _PackageName + "/" + _AssetName;

        internal const string _DebugSymbol = "TUTAN_MESSAGES_DEBUG";
        internal const string _AutoInstallDrainersSymbol = "TUTAN_MESSAGES_AUTOINSTALL_DRAINERS";
        internal const string _AutoInstallCommandBusSymbol = "TUTAN_MESSAGES_AUTOINSTALL_COMMANDBUS";

        [SerializeField] internal bool AutoInstallDrainers = true;
        [SerializeField] internal bool AutoInstallCommandBus = true;
        [SerializeField] internal bool EnableInstrumentation = true;

        public void Save() => Save(saveAsText: true);

        // Re-sync side effects (scripting defines, etc.) with the persisted flags.
        // Called from the settings UI when the user toggles a flag, and on editor
        // load so the project state matches the asset even if it was edited
        // externally (VCS update, hand-edit).
        internal void ApplySideEffects()
        {
            SetDefineSymbol(_DebugSymbol, EnableInstrumentation);
            SetDefineSymbol(_AutoInstallDrainersSymbol, AutoInstallDrainers);
            SetDefineSymbol(_AutoInstallCommandBusSymbol, AutoInstallCommandBus);
        }

        [InitializeOnLoadMethod]
        static void SyncOnLoad() => instance.ApplySideEffects();

        static void SetDefineSymbol(string symbol, bool enabled)
        {
            var target = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));

            var current = PlayerSettings.GetScriptingDefineSymbols(target);
            var symbols = new List<string>(current.Split(';', StringSplitOptions.RemoveEmptyEntries));

            bool has = symbols.Contains(symbol);
            if (enabled == has) return;

            if (enabled) symbols.Add(symbol);
            else symbols.Remove(symbol);

            PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
        }

        // Protected
        protected void OnEnable() => hideFlags &= ~HideFlags.NotEditable;
    }
}
