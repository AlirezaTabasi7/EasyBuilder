using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static EasyBuilder.EditorUI;

namespace EasyBuilder
{
    /// <summary>
    /// Inspector for build profiles, including custom subclasses: their extra fields appear under
    /// "Custom Settings". Several profiles can be edited at once.
    /// </summary>
    [CustomEditor(typeof(BaseBuildProfileSO), true)]
    [CanEditMultipleObjects]
    internal class BaseBuildProfileSOEditor : Editor
    {
        private static readonly HashSet<string> BaseFields = new()
        {
            "m_Script", "packageName", "aabBuild", "splitArchitectures", "overrideTitle", "appTitle", "overrideIcon",
            "adaptiveBackground", "adaptiveForeground", "roundedIcon", "legacyIcon", "scenes", "extraScriptingDefines",
        };

        private static readonly string[] OutputChoices =
        {
            "APK",
            "Split APKs (one per CPU architecture)",
            "AAB (Android App Bundle)",
        };

        private static readonly string[] OutputHints =
        {
            "A single APK for all CPU architectures. Easiest to install for testing.",
            "One smaller APK per CPU architecture, written into a folder.",
            "The format Google Play requires for new apps.",
        };

        private BaseBuildProfileSO[] Profiles => targets.OfType<BaseBuildProfileSO>().ToArray();
        private bool IsMultiple => targets.Length > 1;

        private Label _summary;
        private StatusPanel _status;
        private Button _listButton;
        private Label _packageHint;
        private VisualElement _titleField;
        private Label _titleHint;
        private DropdownField _outputField;
        private Label _outputHint;
        private VisualElement _iconSlots;
        private readonly List<(Image image, SerializedProperty property)> _iconPreviews = new();
        private Label _scenesHint;

        private void OnEnable() => Undo.undoRedoPerformed += Refresh;

        private void OnDisable() => Undo.undoRedoPerformed -= Refresh;

        public override VisualElement CreateInspectorGUI()
        {
            _iconPreviews.Clear();
            var root = new VisualElement();

            root.Add(CreateOverview());
            root.Add(CreateIdentitySection());
            root.Add(CreateAndroidSection());
            root.Add(CreateIconSection());
            root.Add(CreateBuildSection());

            var custom = CreateCustomSection();

            if (custom != null)
                root.Add(custom);

            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                Refresh();
                EasyBuilderWindow.RefreshOpenWindows();
            });

            // Build Settings scenes, the platform and the window's list can change without this asset changing.
            root.schedule.Execute(Refresh).Every(2000);

            Refresh();
            return root;
        }

        private VisualElement CreateOverview()
        {
            var section = CreateSection(GetHeading());

            _summary = CreateHint(string.Empty);
            _summary.style.marginBottom = 6;
            section.Add(_summary);

            _status = new StatusPanel("Ready to build", "to fix before building");
            section.Add(_status);

            _listButton = new Button(ToggleInEasyBuilder);
            var buttons = CreateButtonRow(("Open Easy Builder", "Open the Easy Builder window.", EasyBuilderWindow.Open));
            buttons.Insert(0, _listButton);
            section.Add(buttons);

            return section;
        }

        private string GetHeading()
        {
            if (IsMultiple)
                return $"{targets.Length} Build Profiles";

            if (target.GetType() == typeof(BaseBuildProfileSO))
                return "Build Profile";

            // "SampleBuildProfileSO" reads as "Sample Build Profile".
            var typeName = ObjectNames.NicifyVariableName(target.GetType().Name);

            if (typeName.EndsWith(" SO"))
                typeName = typeName.Substring(0, typeName.Length - 3);

            return typeName;
        }

        private VisualElement CreateIdentitySection()
        {
            var section = CreateSection("App Identity");

            section.Add(new PropertyField(serializedObject.FindProperty("packageName"), "Package Name"));
            _packageHint = CreateHint(string.Empty);
            section.Add(_packageHint);

            section.Add(new PropertyField(serializedObject.FindProperty("overrideTitle"), "Override Product Name"));
            _titleField = new PropertyField(serializedObject.FindProperty("appTitle"), "Product Name");
            section.Add(_titleField);
            _titleHint = CreateHint(string.Empty);
            section.Add(_titleHint);

            return section;
        }

        private VisualElement CreateAndroidSection()
        {
            var section = CreateSection("Android Output", "Used for Android builds only.");

            _outputField = new DropdownField("Format", OutputChoices.ToList(), 0);
            _outputField.RegisterValueChangedCallback(_ =>
            {
                var index = _outputField.index;

                if (index < 0)
                    return;

                serializedObject.Update();
                serializedObject.FindProperty("aabBuild").boolValue = index == 2;
                serializedObject.FindProperty("splitArchitectures").boolValue = index == 1;
                serializedObject.ApplyModifiedProperties();
            });
            section.Add(_outputField);

            _outputHint = CreateHint(string.Empty);
            section.Add(_outputHint);

            return section;
        }

        private VisualElement CreateIconSection()
        {
            var section = CreateSection("Icons (Android)",
                "Used for Android builds only. Every size is generated from these textures; leave a slot empty to keep the project's icon of that kind.");

            section.Add(new PropertyField(serializedObject.FindProperty("overrideIcon"), "Override Icons"));

            _iconSlots = new VisualElement();
            _iconSlots.style.flexDirection = FlexDirection.Row;
            _iconSlots.style.flexWrap = Wrap.Wrap;
            _iconSlots.style.marginTop = 4;
            _iconSlots.Add(CreateIconSlot("Adaptive", "Android 8+. Background and foreground layers.", 12,
                ("Background", "adaptiveBackground"), ("Foreground", "adaptiveForeground")));
            _iconSlots.Add(CreateIconSlot("Round", "Android 7.1 launchers.", 32, ("Texture", "roundedIcon")));
            _iconSlots.Add(CreateIconSlot("Legacy", "Older devices.", 6, ("Texture", "legacyIcon")));
            section.Add(_iconSlots);

            return section;
        }

        private VisualElement CreateIconSlot(string title, string hint, float previewRadius, params (string label, string property)[] layers)
        {
            var card = new VisualElement();
            card.style.flexGrow = 1;
            card.style.flexBasis = 110;
            card.style.minWidth = 110;
            card.style.marginRight = card.style.marginBottom = 4;
            card.style.paddingTop = card.style.paddingBottom = card.style.paddingLeft = card.style.paddingRight = 4;
            SetBorder(card, 1, BorderColor, 4);

            var heading = new Label(title);
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            card.Add(heading);

            // Layers stack on top of each other, like the launcher draws them.
            var preview = new VisualElement();
            preview.style.width = preview.style.height = 64;
            preview.style.alignSelf = Align.Center;
            preview.style.marginTop = preview.style.marginBottom = 4;
            preview.style.overflow = Overflow.Hidden;
            preview.style.backgroundColor = new Color(0, 0, 0, 0.2f);
            SetBorder(preview, 1, BorderColor, previewRadius);
            card.Add(preview);

            foreach (var (label, propertyName) in layers)
            {
                var property = serializedObject.FindProperty(propertyName);

                var image = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
                image.style.position = Position.Absolute;
                image.style.left = image.style.top = image.style.right = image.style.bottom = 0;
                preview.Add(image);
                _iconPreviews.Add((image, property));

                var caption = new Label(label);
                caption.style.fontSize = 10;
                caption.style.paddingLeft = 3;
                card.Add(caption);

                card.Add(new ObjectField { objectType = typeof(Texture2D), allowSceneObjects = false, bindingPath = propertyName });
            }

            var hintLabel = CreateHint(hint);
            hintLabel.style.marginTop = 2;
            card.Add(hintLabel);

            return card;
        }

        private VisualElement CreateBuildSection()
        {
            var section = CreateSection("Build");

            section.Add(new PropertyField(serializedObject.FindProperty("scenes"), "Scenes"));
            _scenesHint = CreateHint(string.Empty);
            section.Add(_scenesHint);
            section.Add(CreateButtonRow(("Copy Build Settings Scenes",
                "Fill the list with the scenes enabled in Build Settings, so you can edit it.", CopyBuildSettingsScenes)));

            var defines = new PropertyField(serializedObject.FindProperty("extraScriptingDefines"), "Extra Scripting Defines");
            defines.style.marginTop = 6;
            section.Add(defines);
            section.Add(CreateHint("Added to the project's define symbols for this profile's build only, e.g. STORE_GOOGLE."));

            return section;
        }

        private VisualElement CreateCustomSection()
        {
            var fields = new List<VisualElement>();
            var property = serializedObject.GetIterator();

            for (var enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
                if (!BaseFields.Contains(property.name))
                    fields.Add(new PropertyField(property.Copy()));

            if (fields.Count == 0)
                return null;

            var section = CreateSection("Custom Settings", $"Fields added by {target.GetType().Name}.");

            foreach (var field in fields)
                section.Add(field);

            return section;
        }

        private void Refresh()
        {
            if (_summary == null || target == null || serializedObject.targetObject == null)
                return;

            serializedObject.Update();

            var buildTarget = BuildRunner.ActiveTarget;
            var profiles = Profiles;

            _summary.text = IsMultiple
                ? $"{profiles.Length} profiles selected. Edits apply to all of them."
                : profiles[0].Describe(buildTarget);

            _status.SetProblems(IsMultiple
                ? profiles.SelectMany(profile => BuildRunner.ValidateProfile(profile, buildTarget).Select(problem => $"{profile.name}: {problem}")).ToList()
                : BuildRunner.ValidateProfile(profiles[0], buildTarget));

            var settings = EasyBuilderSettings.instance;
            var allInList = profiles.All(settings.ContainsProfile);
            _listButton.text = allInList
                ? (IsMultiple ? "Remove All from Easy Builder" : "Remove from Easy Builder")
                : (IsMultiple ? "Add All to Easy Builder" : "Add to Easy Builder");
            _listButton.tooltip = allInList
                ? "Remove from the Easy Builder window's list. The assets are kept."
                : "Add to the Easy Builder window's list.";

            var packageName = serializedObject.FindProperty("packageName");
            _packageHint.text = packageName.hasMultipleDifferentValues ? "The selected profiles have different package names."
                : string.IsNullOrEmpty(packageName.stringValue) ? $"Empty: keeps the project's ID ({PlayerSettings.GetApplicationIdentifier(BuildRunner.ActiveNamedTarget)})."
                : "Replaces the project's application ID for this build, e.g. com.company.game.demo.";

            var overrideTitle = serializedObject.FindProperty("overrideTitle");
            _titleField.SetEnabled(overrideTitle.boolValue || overrideTitle.hasMultipleDifferentValues);
            _titleHint.text = $"Project's product name: {PlayerSettings.productName}";

            var aab = serializedObject.FindProperty("aabBuild");
            var split = serializedObject.FindProperty("splitArchitectures");
            var mixedOutput = aab.hasMultipleDifferentValues || (!aab.boolValue && split.hasMultipleDifferentValues);
            var outputIndex = aab.boolValue ? 2 : split.boolValue ? 1 : 0;

            // Setting a value clears the mixed state, so set it first.
            _outputField.SetValueWithoutNotify(OutputChoices[outputIndex]);
            _outputField.showMixedValue = mixedOutput;
            _outputHint.text = mixedOutput ? "The selected profiles use different formats." : OutputHints[outputIndex];

            var overrideIcon = serializedObject.FindProperty("overrideIcon");
            _iconSlots.SetEnabled(overrideIcon.boolValue || overrideIcon.hasMultipleDifferentValues);

            foreach (var (image, property) in _iconPreviews)
                image.image = property.hasMultipleDifferentValues ? null : property.objectReferenceValue as Texture2D;

            var buildSettingsScenes = BuildRunner.GetBuildSettingsScenes().Length;
            var scenes = serializedObject.FindProperty("scenes");
            _scenesHint.text = scenes.hasMultipleDifferentValues ? "The selected profiles have different scene lists."
                : scenes.arraySize == 0 ? $"Empty: builds the {buildSettingsScenes} scene{(buildSettingsScenes == 1 ? "" : "s")} enabled in Build Settings."
                : "Only these scenes are built for this profile; the first one opens at launch.";
        }

        private void ToggleInEasyBuilder()
        {
            var settings = EasyBuilderSettings.instance;
            var profiles = Profiles;

            if (profiles.All(settings.ContainsProfile))
            {
                foreach (var profile in profiles)
                    settings.RemoveProfile(profile);
            }
            else
            {
                foreach (var profile in profiles)
                    settings.AddProfile(profile);
            }

            EasyBuilderWindow.RefreshOpenWindows();
            Refresh();
        }

        private void CopyBuildSettingsScenes()
        {
            var scenes = BuildRunner.GetBuildSettingsScenes()
                .Select(AssetDatabase.LoadAssetAtPath<SceneAsset>)
                .Where(scene => scene != null)
                .ToArray();

            if (scenes.Length == 0)
            {
                EditorUtility.DisplayDialog("Copy Build Settings Scenes",
                    "No scenes are enabled in Build Settings. Add scenes there first, or drag scenes into the list.", "OK");
                return;
            }

            serializedObject.Update();
            var property = serializedObject.FindProperty("scenes");
            property.arraySize = scenes.Length;

            for (var i = 0; i < scenes.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = scenes[i];

            serializedObject.ApplyModifiedProperties();
        }
    }
}
