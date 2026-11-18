using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EasyBuilder
{
    /// <summary>
    /// Records the project settings that Easy Builder and <see cref="BaseBuildProfileSO"/> change, so they
    /// can be put back exactly as they were after a build, however the build ended.
    /// </summary>
    public sealed class BuildSettingsSnapshot
    {
        private NamedBuildTarget _target;
        private string _applicationIdentifier;
        private string _productName;

        private bool _buildAppBundle;
        private bool _splitApks;

        private bool _useCustomKeystore;
        private string _keystoreName;
        private string _keystorePass;
        private string _keyaliasName;
        private string _keyaliasPass;

        // Android icon textures per icon kind, per size slot, per layer.
        private readonly List<(PlatformIconKind kind, Texture2D[][] textures)> _androidIcons = new();

        private BuildSettingsSnapshot() { }

        public static BuildSettingsSnapshot Capture(NamedBuildTarget target)
        {
            var snapshot = new BuildSettingsSnapshot
            {
                _target = target,
                _applicationIdentifier = PlayerSettings.GetApplicationIdentifier(target),
                _productName = PlayerSettings.productName,
                _buildAppBundle = EditorUserBuildSettings.buildAppBundle,
                _splitApks = PlayerSettings.Android.buildApkPerCpuArchitecture,
                _useCustomKeystore = PlayerSettings.Android.useCustomKeystore,
                _keystoreName = PlayerSettings.Android.keystoreName,
                _keystorePass = PlayerSettings.Android.keystorePass,
                _keyaliasName = PlayerSettings.Android.keyaliasName,
                _keyaliasPass = PlayerSettings.Android.keyaliasPass,
            };

            if (BuildRunner.IsModuleInstalled(BuildTarget.Android))
            {
                foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
                {
                    var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                    var textures = new Texture2D[icons.Length][];

                    for (var i = 0; i < icons.Length; i++)
                        textures[i] = icons[i].GetTextures();

                    snapshot._androidIcons.Add((kind, textures));
                }
            }

            return snapshot;
        }

        public void Restore()
        {
            PlayerSettings.SetApplicationIdentifier(_target, _applicationIdentifier);
            PlayerSettings.productName = _productName;

            EditorUserBuildSettings.buildAppBundle = _buildAppBundle;
            PlayerSettings.Android.buildApkPerCpuArchitecture = _splitApks;

            PlayerSettings.Android.useCustomKeystore = _useCustomKeystore;
            PlayerSettings.Android.keystoreName = _keystoreName;
            PlayerSettings.Android.keystorePass = _keystorePass;
            PlayerSettings.Android.keyaliasName = _keyaliasName;
            PlayerSettings.Android.keyaliasPass = _keyaliasPass;

            foreach (var (kind, textures) in _androidIcons)
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);

                for (var i = 0; i < icons.Length && i < textures.Length; i++)
                    icons[i].SetTextures(textures[i]);

                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
        }
    }
}
