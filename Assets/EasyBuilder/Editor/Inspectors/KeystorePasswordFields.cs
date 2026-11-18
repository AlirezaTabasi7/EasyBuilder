using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using static EasyBuilder.EditorUI;

namespace EasyBuilder
{
    /// <summary>
    /// Edits a keystore profile's passwords. They are saved in this machine's EditorPrefs, never in the
    /// asset. Each field shows where the password will actually be read from. Every open copy of this
    /// element (in the window and in the Inspector) stays in sync.
    /// </summary>
    internal sealed class KeystorePasswordFields : VisualElement
    {
        // Lets the copy in the window and the copy in the Inspector update each other.
        private static event Action<KeystoreProfileSO> PasswordsChanged;

        private readonly TextField _storePasswordField;
        private readonly TextField _aliasPasswordField;
        private readonly Label _storePasswordStatus;
        private readonly Label _aliasPasswordStatus;
        private readonly Button _forgetButton;
        private readonly VisualElement _legacyBox;
        private KeystoreProfileSO _keystore;

        /// <summary>Raised after the passwords change, here or in another copy of this element.</summary>
        public event Action Changed;

        public KeystorePasswordFields()
        {
            _storePasswordField = CreatePasswordField("Keystore Password", value => _keystore.LocalPassword = value);
            _storePasswordStatus = CreateStatusLabel();
            _aliasPasswordField = CreatePasswordField("Key Alias Password", value => _keystore.LocalAliasPassword = value);
            _aliasPasswordStatus = CreateStatusLabel();

            var revealToggle = new Toggle("Show Passwords");
            revealToggle.RegisterValueChangedCallback(evt =>
            {
                _storePasswordField.isPasswordField = !evt.newValue;
                _aliasPasswordField.isPasswordField = !evt.newValue;
            });

            _forgetButton = new Button(ForgetPasswords)
            {
                text = "Forget on This Machine",
                tooltip = "Delete the passwords saved in this machine's EditorPrefs.",
            };

            var revealRow = new VisualElement();
            revealRow.style.flexDirection = FlexDirection.Row;
            revealRow.style.justifyContent = Justify.SpaceBetween;
            revealRow.style.alignItems = Align.Center;
            revealRow.style.flexWrap = Wrap.Wrap;
            revealRow.Add(revealToggle);
            revealRow.Add(_forgetButton);

            _legacyBox = new VisualElement();
            _legacyBox.style.marginTop = 4;
            _legacyBox.Add(new HelpBox(
                "This asset still contains passwords in plain text, saved by an older version of Easy Builder. " +
                "Anyone who can read the repository can read them.",
                HelpBoxMessageType.Warning));
            _legacyBox.Add(new Button(MovePasswordsOutOfAsset) { text = "Move Passwords to This Machine" });

            Add(_storePasswordField);
            Add(_storePasswordStatus);
            Add(_aliasPasswordField);
            Add(_aliasPasswordStatus);
            Add(revealRow);
            Add(_legacyBox);

            RegisterCallback<AttachToPanelEvent>(_ => PasswordsChanged += OnPasswordsChangedElsewhere);
            RegisterCallback<DetachFromPanelEvent>(_ => PasswordsChanged -= OnPasswordsChangedElsewhere);
        }

        public void SetKeystore(KeystoreProfileSO keystore)
        {
            _keystore = keystore;
            Refresh();
        }

        public void Refresh()
        {
            SetVisible(this, _keystore != null);

            if (_keystore == null)
                return;

            ShowValue(_storePasswordField, _keystore.LocalPassword);
            ShowValue(_aliasPasswordField, _keystore.LocalAliasPassword);
            ShowStatus(_storePasswordStatus, _keystore.PasswordSource, _keystore.PasswordEnvironmentVariable);
            ShowStatus(_aliasPasswordStatus, _keystore.AliasPasswordSource, _keystore.AliasPasswordEnvironmentVariable);
            _forgetButton.SetEnabled(!string.IsNullOrEmpty(_keystore.LocalPassword) || !string.IsNullOrEmpty(_keystore.LocalAliasPassword));
            SetVisible(_legacyBox, _keystore.HasPasswordsInAsset);
        }

        // Never rewrite a field while it's being typed in; that would move the cursor.
        private static void ShowValue(TextField field, string value)
        {
            value ??= string.Empty;

            if (field.value != value && !HasFocus(field))
                field.SetValueWithoutNotify(value);
        }

        private TextField CreatePasswordField(string label, Action<string> save)
        {
            var field = new TextField(label) { isPasswordField = true };
            field.RegisterValueChangedCallback(evt =>
            {
                if (_keystore == null)
                    return;

                save(evt.newValue);
                NotifyChanged();
            });
            return field;
        }

        private void NotifyChanged()
        {
            Refresh();
            Changed?.Invoke();
            PasswordsChanged?.Invoke(_keystore);
        }

        private void OnPasswordsChangedElsewhere(KeystoreProfileSO keystore)
        {
            if (keystore != _keystore)
                return;

            Refresh();
            Changed?.Invoke();
        }

        private static Label CreateStatusLabel()
        {
            var label = new Label();
            label.style.fontSize = 10;
            label.style.marginBottom = 4;
            label.style.paddingLeft = 3;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static void ShowStatus(Label label, KeystoreProfileSO.SecretSource source, string environmentVariable)
        {
            switch (source)
            {
                case KeystoreProfileSO.SecretSource.EnvironmentVariable:
                    label.text = $"✓ Read from the {environmentVariable} environment variable, which takes priority over this field.";
                    label.style.color = OkColor;
                    break;
                case KeystoreProfileSO.SecretSource.ThisMachine:
                    label.text = "✓ Saved on this machine (EditorPrefs). Not stored in the asset.";
                    label.style.color = OkColor;
                    break;
                case KeystoreProfileSO.SecretSource.Asset:
                    label.text = "⚠ Read from the asset in plain text. Move it to this machine below.";
                    label.style.color = WarningColor;
                    break;
                default:
                    label.text = string.IsNullOrEmpty(environmentVariable)
                        ? "✗ Not set."
                        : $"✗ Not set. Enter it here, or set {environmentVariable} on build machines.";
                    label.style.color = ErrorColor;
                    break;
            }
        }

        private void ForgetPasswords()
        {
            if (_keystore == null ||
                !EditorUtility.DisplayDialog("Forget Passwords",
                    $"Delete the passwords for '{_keystore.name}' saved on this machine?", "Forget", "Cancel"))
                return;

            _keystore.LocalPassword = null;
            _keystore.LocalAliasPassword = null;

            // The fields aren't being typed in, so show the cleared values right away.
            _storePasswordField.SetValueWithoutNotify(string.Empty);
            _aliasPasswordField.SetValueWithoutNotify(string.Empty);
            NotifyChanged();
        }

        private void MovePasswordsOutOfAsset()
        {
            if (_keystore == null || !_keystore.MovePasswordsOutOfAsset())
                return;

            NotifyChanged();
        }
    }
}
