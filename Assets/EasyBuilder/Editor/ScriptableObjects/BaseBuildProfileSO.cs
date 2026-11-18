using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace EasyBuilder
{
    /// <summary>
    /// A build profile. To add your own settings, inherit from this class and override
    /// <see cref="ApplyProfile"/> and <see cref="RevertProfile"/> (calling the base versions), and optionally
    /// <see cref="ConfigureBuildOptions"/> and <see cref="Validate"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Easy Builder/New Base Profile")]
    public class BaseBuildProfileSO : ScriptableObject, IBuildProfile
    {
        private static readonly Regex AndroidIdentifierPattern = new(@"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$");
        private static readonly Regex IosIdentifierPattern = new(@"^[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)+$");
        private static readonly Regex DefineSymbolPattern = new(@"^[A-Za-z_][A-Za-z0-9_]*$");

        [Tooltip("Application identifier for this profile. Leave empty to keep the project's.")]
        [SerializeField] private string packageName;

        [Header("Android")]
        [SerializeField] private bool aabBuild;
        [Tooltip("Build one APK per CPU architecture. Ignored for AAB builds.")]
        [SerializeField] private bool splitArchitectures = true;

        [Header("App Title")]
        [SerializeField] private bool overrideTitle;
        [SerializeField] private string appTitle;

        [Header("Icon (Android)")]
        [Tooltip("Replace the Android icons for this profile. A texture left empty keeps the project's icon of that kind.")]
        [SerializeField] private bool overrideIcon;
        [SerializeField] private Texture2D adaptiveBackground;
        [SerializeField] private Texture2D adaptiveForeground;
        [SerializeField] private Texture2D roundedIcon;
        [SerializeField] private Texture2D legacyIcon;

        [Header("Build")]
        [Tooltip("Scenes to build for this profile. Leave empty to use the scenes enabled in Build Settings.")]
        [SerializeField] private SceneAsset[] scenes = Array.Empty<SceneAsset>();
        [Tooltip("Scripting define symbols added for this profile's build only.")]
        [SerializeField] private string[] extraScriptingDefines = Array.Empty<string>();

        [NonSerialized] private BuildSettingsSnapshot _snapshot;

        /// <summary>
        /// Applies this profile's settings. Overrides should call the base version first: it records the
        /// current settings so <see cref="RevertProfile"/> can restore them.
        /// </summary>
        public virtual void ApplyProfile()
        {
            var target = BuildRunner.ActiveTarget;

            // If an earlier apply was never reverted, keep its snapshot: it holds the real originals.
            _snapshot ??= BuildSettingsSnapshot.Capture(BuildRunner.ActiveNamedTarget);

            Debug.Log($"[EasyBuilder] Applying profile '{name}'...");

            if (!string.IsNullOrEmpty(packageName))
                PlayerSettings.SetApplicationIdentifier(BuildRunner.ActiveNamedTarget, packageName);

            if (target == BuildTarget.Android)
            {
                EditorUserBuildSettings.buildAppBundle = aabBuild;
                PlayerSettings.Android.buildApkPerCpuArchitecture = splitArchitectures && !aabBuild;
                ApplyIcons();
            }
            else if (overrideIcon)
            {
                Debug.LogWarning($"[EasyBuilder] '{name}': the icon override only supports Android, so it is skipped for {target}.");
            }

            if (overrideTitle && !string.IsNullOrEmpty(appTitle))
                PlayerSettings.productName = appTitle;

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Restores the settings recorded by <see cref="ApplyProfile"/>. Overrides should undo their own
        /// changes and then call the base version.
        /// </summary>
        public virtual void RevertProfile()
        {
            var snapshot = _snapshot;
            _snapshot = null;

            if (snapshot == null)
                return;

            snapshot.Restore();
            AssetDatabase.SaveAssets();

            Debug.Log($"[EasyBuilder] Reverted profile '{name}'.");
        }

        /// <summary>
        /// Adjusts the options this profile is built with. Called after <see cref="ApplyProfile"/>.
        /// The base version applies the profile's scene list and extra scripting defines.
        /// </summary>
        public virtual void ConfigureBuildOptions(ref BuildPlayerOptions options)
        {
            var sceneOverride = GetSceneOverride();

            if (sceneOverride != null)
                options.scenes = sceneOverride;

            var defines = GetExtraScriptingDefines();

            if (defines.Length > 0)
                options.extraScriptingDefines = (options.extraScriptingDefines ?? Array.Empty<string>()).Concat(defines).Distinct().ToArray();
        }

        /// <summary>
        /// Adds a message to <paramref name="errors"/> for each problem that would make the build fail or
        /// come out wrong. Runs before any profile in the batch is built.
        /// </summary>
        public virtual void Validate(BuildTarget target, List<string> errors)
        {
            if (!string.IsNullOrEmpty(packageName))
            {
                var pattern = target == BuildTarget.iOS ? IosIdentifierPattern : AndroidIdentifierPattern;

                if (!pattern.IsMatch(packageName))
                    errors.Add($"'{packageName}' is not a valid application identifier for {target}.");
            }

            if (overrideTitle && string.IsNullOrWhiteSpace(appTitle))
                errors.Add("'Override Title' is on but 'App Title' is empty.");

            if (overrideIcon && target == BuildTarget.Android)
            {
                if (adaptiveBackground == null && adaptiveForeground == null && roundedIcon == null && legacyIcon == null)
                    errors.Add("'Override Icon' is on but no icon textures are set.");

                if ((adaptiveBackground == null) != (adaptiveForeground == null))
                    errors.Add("The adaptive icon needs both a background and a foreground texture.");
            }

            if (scenes != null && scenes.Any(scene => scene == null))
                errors.Add("The scene list has an empty entry.");

            foreach (var define in GetExtraScriptingDefines())
                if (!DefineSymbolPattern.IsMatch(define))
                    errors.Add($"'{define}' is not a valid scripting define symbol.");
        }

        /// <summary>A one-line description of what this profile changes, shown in the Easy Builder window.</summary>
        public virtual string Describe(BuildTarget target)
        {
            var parts = new List<string> { string.IsNullOrEmpty(packageName) ? "Project package name" : packageName };

            if (target == BuildTarget.Android)
                parts.Add(aabBuild ? "AAB" : splitArchitectures ? "Split APKs" : "APK");

            if (overrideTitle && !string.IsNullOrEmpty(appTitle))
                parts.Add($"Title \"{appTitle}\"");

            if (overrideIcon)
                parts.Add("Custom icons");

            var sceneOverride = GetSceneOverride();
            parts.Add(sceneOverride == null ? "Build Settings scenes" : $"{sceneOverride.Length} scene{(sceneOverride.Length == 1 ? "" : "s")}");

            var defines = GetExtraScriptingDefines();

            if (defines.Length > 0)
                parts.Add("Defines: " + string.Join(", ", defines));

            return string.Join("  ·  ", parts);
        }

        /// <summary>The scene paths this profile builds, or null to use the Build Settings scenes.</summary>
        public string[] GetSceneOverride()
        {
            if (scenes == null || scenes.Length == 0)
                return null;

            return scenes.Where(scene => scene != null).Select(AssetDatabase.GetAssetPath).ToArray();
        }

        private string[] GetExtraScriptingDefines() =>
            (extraScriptingDefines ?? Array.Empty<string>())
            .Select(define => define?.Trim())
            .Where(define => !string.IsNullOrEmpty(define))
            .ToArray();

        private void ApplyIcons()
        {
            if (!overrideIcon)
                return;

            if (!BuildRunner.IsModuleInstalled(BuildTarget.Android))
            {
                Debug.LogWarning($"[EasyBuilder] '{name}': Android Build Support isn't installed, so the icons can't be replaced.");
                return;
            }

            Debug.Log($"[EasyBuilder] Applying '{name}' icons...");

            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                var textures = GetIconTextures(kind);

                if (textures == null)
                    continue;

                // Fill every size slot; Unity scales the texture for each density. Setting only the largest
                // slot left the project's old icon on lower-density devices.
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);

                foreach (var icon in icons)
                    icon.SetTextures(textures);

                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
        }

        // Kind names have a suffix in some editor versions, such as "Adaptive (API 26)", so match the start.
        private Texture2D[] GetIconTextures(PlatformIconKind kind)
        {
            var kindName = kind.ToString();

            if (kindName.StartsWith("Adaptive"))
                return adaptiveBackground != null && adaptiveForeground != null ? new[] { adaptiveBackground, adaptiveForeground } : null;

            if (kindName.StartsWith("Round"))
                return roundedIcon != null ? new[] { roundedIcon } : null;

            if (kindName.StartsWith("Legacy"))
                return legacyIcon != null ? new[] { legacyIcon } : null;

            return null;
        }
    }
}
