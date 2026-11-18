using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using static EasyBuilder.EditorUI;

namespace EasyBuilder
{
    [CustomEditor(typeof(KeystoreProfileSO))]
    [CanEditMultipleObjects]
    internal class KeystoreProfileSOEditor : Editor
    {
        private KeystoreProfileSO Keystore => (KeystoreProfileSO)target;

        private StatusPanel _status;
        private Button _useButton;
        private Label _fileStatus;
        private KeystorePasswordFields _passwords;

        private void OnEnable() => Undo.undoRedoPerformed += Refresh;

        private void OnDisable() => Undo.undoRedoPerformed -= Refresh;

        public override VisualElement CreateInspectorGUI()
        {
            if (targets.Length > 1)
                return CreateMultipleSelectionGUI();

            var root = new VisualElement();

            root.Add(CreateOverview());
            root.Add(CreateFileSection());
            root.Add(CreatePasswordSection());
            root.Add(CreateCiSection());

            root.TrackSerializedObjectValue(serializedObject, _ =>
            {
                Refresh();
                EasyBuilderWindow.RefreshOpenWindows();
            });

            // The keystore file and environment variables can change outside Unity.
            root.schedule.Execute(Refresh).Every(2000);

            Refresh();
            return root;
        }

        // Paths and aliases can be edited together; passwords belong to one keystore at a time.
        private VisualElement CreateMultipleSelectionGUI()
        {
            var section = CreateSection($"{targets.Length} Keystore Profiles", "Edits apply to all of them.");
            section.Add(new PropertyField(serializedObject.FindProperty("path"), "Keystore File"));
            section.Add(new PropertyField(serializedObject.FindProperty("aliasName"), "Key Alias"));
            section.Add(new PropertyField(serializedObject.FindProperty("passwordEnvironmentVariable"), "Keystore Password Variable"));
            section.Add(new PropertyField(serializedObject.FindProperty("aliasPasswordEnvironmentVariable"), "Alias Password Variable"));
            section.Add(new HelpBox("Select a single keystore profile to enter its passwords.", HelpBoxMessageType.Info));
            return section;
        }

        private VisualElement CreateOverview()
        {
            var section = CreateSection("Keystore Profile", "Signs Android builds. The asset stores only the file path and alias.");

            _status = new StatusPanel("Ready to sign builds", "to fix before signing");
            section.Add(_status);

            _useButton = new Button(UseInEasyBuilder);
            var buttons = CreateButtonRow(("Open Easy Builder", "Open the Easy Builder window.", EasyBuilderWindow.Open));
            buttons.Insert(0, _useButton);
            section.Add(buttons);

            return section;
        }

        private VisualElement CreateFileSection()
        {
            var section = CreateSection("Keystore");

            var pathProperty = serializedObject.FindProperty("path");
            var pathField = new PropertyField(pathProperty, "Keystore File");
            section.Add(WithSideButtons(pathField, ("Browse…", "Pick a .keystore or .jks file.", () => BrowseForKeystore(pathProperty))));

            _fileStatus = CreateHint(string.Empty);
            _fileStatus.style.opacity = 1;
            section.Add(_fileStatus);

            section.Add(new PropertyField(serializedObject.FindProperty("aliasName"), "Key Alias"));
            section.Add(CreateHint("The name of the key inside the keystore that signs the app."));

            return section;
        }

        private VisualElement CreatePasswordSection()
        {
            var section = CreateSection("Passwords",
                "Saved in this machine's EditorPrefs, never in the asset, so they stay out of version control.");

            _passwords = new KeystorePasswordFields();
            _passwords.SetKeystore(Keystore);
            _passwords.Changed += Refresh;
            section.Add(_passwords);

            return section;
        }

        private VisualElement CreateCiSection()
        {
            var section = CreateSection("Build Machines (CI)");

            // The view data key remembers whether the foldout was open.
            var foldout = new Foldout { text = "Environment Variables", value = false, viewDataKey = "EasyBuilderKeystoreCiFoldout" };
            foldout.Add(CreateHint("Set these variables on build machines instead of entering passwords. They take priority over the passwords above."));
            foldout.Add(new PropertyField(serializedObject.FindProperty("passwordEnvironmentVariable"), "Keystore Password"));
            foldout.Add(new PropertyField(serializedObject.FindProperty("aliasPasswordEnvironmentVariable"), "Key Alias Password"));
            section.Add(foldout);

            return section;
        }

        private void Refresh()
        {
            if (_status == null || target == null)
                return;

            _passwords.Refresh();

            var problems = new List<string>();
            Keystore.Validate(problems);
            _status.SetProblems(problems);

            var inUse = EasyBuilderSettings.instance.Keystore == Keystore;
            _useButton.text = inUse ? "✓ Used by Easy Builder" : "Use in Easy Builder";
            _useButton.tooltip = inUse
                ? "Easy Builder signs Android builds with this keystore."
                : "Make Easy Builder sign Android builds with this keystore.";
            _useButton.SetEnabled(!inUse);

            var path = Keystore.Path;

            if (string.IsNullOrWhiteSpace(path))
            {
                _fileStatus.text = "✗ No file set. Click Browse… to pick your keystore.";
                _fileStatus.style.color = ErrorColor;
            }
            else if (!File.Exists(path))
            {
                _fileStatus.text = $"✗ File not found: {Path.GetFullPath(path)}";
                _fileStatus.style.color = ErrorColor;
            }
            else
            {
                _fileStatus.text = Path.IsPathRooted(path)
                    ? "✓ Found. The path is absolute, so it only works on machines with the same folder layout."
                    : "✓ Found, relative to the project folder.";
                _fileStatus.style.color = Path.IsPathRooted(path) ? WarningColor : OkColor;
            }
        }

        private void UseInEasyBuilder()
        {
            EasyBuilderSettings.instance.SetKeystore(Keystore);
            EasyBuilderWindow.RefreshOpenWindows();
            Refresh();
        }

        private void BrowseForKeystore(SerializedProperty pathProperty)
        {
            var current = pathProperty.stringValue;
            var startFolder = string.IsNullOrEmpty(current) ? string.Empty : Path.GetDirectoryName(Path.GetFullPath(current));
            var selected = EditorUtility.OpenFilePanelWithFilters("Select keystore", startFolder, new[] { "Keystore", "keystore,jks", "All files", "*" });

            if (string.IsNullOrEmpty(selected))
                return;

            // Store keystores inside the project as relative paths so the profile works on every machine.
            var projectFolder = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/') + "/";
            selected = selected.Replace('\\', '/');

            if (selected.StartsWith(projectFolder, StringComparison.OrdinalIgnoreCase))
                selected = selected.Substring(projectFolder.Length);

            serializedObject.Update();
            pathProperty.stringValue = selected;
            serializedObject.ApplyModifiedProperties();
        }
    }
}
