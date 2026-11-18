using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyBuilder
{
    /// <summary>
    /// An Android signing keystore. The path and alias are stored in the asset; the passwords are not.
    /// They come from environment variables (for CI) or from this machine's EditorPrefs.
    /// </summary>
    [CreateAssetMenu(menuName = "Easy Builder/New Keystore Profile")]
    public class KeystoreProfileSO : ScriptableObject
    {
        [Tooltip("Keystore file (.keystore or .jks), absolute or relative to the project folder.")]
        [SerializeField] private string path;
        [SerializeField] private string aliasName;

        [Tooltip("Environment variable holding the keystore password. Checked before the password saved on this machine; use it on CI.")]
        [SerializeField] private string passwordEnvironmentVariable = "EASYBUILDER_KEYSTORE_PASS";
        [Tooltip("Environment variable holding the key alias password.")]
        [SerializeField] private string aliasPasswordEnvironmentVariable = "EASYBUILDER_KEYALIAS_PASS";

        // Up to v1.2 the passwords were saved here, in plain text inside the asset. They are still read as
        // a last resort so existing profiles keep working; the inspector offers to move them out.
        [SerializeField, HideInInspector] private string password;
        [SerializeField, HideInInspector] private string aliasPassword;

        public string Path => path;
        public string AliasName => aliasName;
        public string Password => ResolveSecret(passwordEnvironmentVariable, StorePasswordKey, password);
        public string AliasPassword => ResolveSecret(aliasPasswordEnvironmentVariable, AliasPasswordKey, aliasPassword);

        /// <summary>Where a password is read from, in priority order.</summary>
        public enum SecretSource
        {
            NotSet,
            EnvironmentVariable,
            ThisMachine,
            Asset,
        }

        public SecretSource PasswordSource => GetSource(passwordEnvironmentVariable, StorePasswordKey, password);
        public SecretSource AliasPasswordSource => GetSource(aliasPasswordEnvironmentVariable, AliasPasswordKey, aliasPassword);
        public string PasswordEnvironmentVariable => passwordEnvironmentVariable;
        public string AliasPasswordEnvironmentVariable => aliasPasswordEnvironmentVariable;

        /// <summary>True when the asset still holds passwords saved by an older version.</summary>
        public bool HasPasswordsInAsset => !string.IsNullOrEmpty(password) || !string.IsNullOrEmpty(aliasPassword);

        /// <summary>The keystore password saved in this machine's EditorPrefs.</summary>
        public string LocalPassword
        {
            get => GetPref(StorePasswordKey);
            set => SetPref(StorePasswordKey, value);
        }

        /// <summary>The key alias password saved in this machine's EditorPrefs.</summary>
        public string LocalAliasPassword
        {
            get => GetPref(AliasPasswordKey);
            set => SetPref(AliasPasswordKey, value);
        }

        private string PrefsKeyPrefix
        {
            get
            {
                var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(this));
                return string.IsNullOrEmpty(guid) ? null : "EasyBuilder.Keystore." + guid;
            }
        }

        private string StorePasswordKey => PrefsKeyPrefix is { } prefix ? prefix + ".StorePass" : null;
        private string AliasPasswordKey => PrefsKeyPrefix is { } prefix ? prefix + ".AliasPass" : null;

        public void Apply()
        {
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = path;
            PlayerSettings.Android.keystorePass = Password;
            PlayerSettings.Android.keyaliasName = aliasName;
            PlayerSettings.Android.keyaliasPass = AliasPassword;
        }

        public void Validate(List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(path))
                errors.Add("The keystore path is empty.");
            else if (!File.Exists(path))
                errors.Add($"The keystore file was not found: {path}");

            if (string.IsNullOrWhiteSpace(aliasName))
                errors.Add("The key alias name is empty.");

            if (string.IsNullOrEmpty(Password))
                errors.Add("The keystore password is not set. " + DescribePasswordSources(passwordEnvironmentVariable));

            if (string.IsNullOrEmpty(AliasPassword))
                errors.Add("The key alias password is not set. " + DescribePasswordSources(aliasPasswordEnvironmentVariable));
        }

        /// <summary>
        /// Copies passwords saved by an older version into this machine's EditorPrefs and removes them
        /// from the asset.
        /// </summary>
        public bool MovePasswordsOutOfAsset()
        {
            if (PrefsKeyPrefix == null)
                return false;

            if (!string.IsNullOrEmpty(password))
                LocalPassword = password;

            if (!string.IsNullOrEmpty(aliasPassword))
                LocalAliasPassword = aliasPassword;

            password = string.Empty;
            aliasPassword = string.Empty;

            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssetIfDirty(this);
            return true;
        }

        /// <summary>Turns off the custom keystore in Player Settings. Easy Builder itself no longer calls this.</summary>
        public static void ClearKeystore()
        {
            PlayerSettings.Android.useCustomKeystore = false;
        }

        private static string ResolveSecret(string environmentVariable, string prefsKey, string legacyValue)
        {
            if (!string.IsNullOrEmpty(environmentVariable))
            {
                var fromEnvironment = Environment.GetEnvironmentVariable(environmentVariable);

                if (!string.IsNullOrEmpty(fromEnvironment))
                    return fromEnvironment;
            }

            var fromPrefs = GetPref(prefsKey);
            return !string.IsNullOrEmpty(fromPrefs) ? fromPrefs : legacyValue;
        }

        private static SecretSource GetSource(string environmentVariable, string prefsKey, string legacyValue)
        {
            if (!string.IsNullOrEmpty(environmentVariable) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(environmentVariable)))
                return SecretSource.EnvironmentVariable;

            if (!string.IsNullOrEmpty(GetPref(prefsKey)))
                return SecretSource.ThisMachine;

            return string.IsNullOrEmpty(legacyValue) ? SecretSource.NotSet : SecretSource.Asset;
        }

        private static string DescribePasswordSources(string environmentVariable) =>
            string.IsNullOrEmpty(environmentVariable)
                ? "Enter it in the password field."
                : $"Enter it in the password field, or set the {environmentVariable} environment variable.";

        private static string GetPref(string key) => key == null ? string.Empty : EditorPrefs.GetString(key, string.Empty);

        private static void SetPref(string key, string value)
        {
            if (key == null)
                return;

            if (string.IsNullOrEmpty(value))
                EditorPrefs.DeleteKey(key);
            else
                EditorPrefs.SetString(key, value);
        }
    }
}
