# **EASY BUILDER**

## **Overview**
EASY BUILDER is a lightweight automation tool that streamlines the Unity build workflow.
Instead of manually changing settings for every build, you set the version, build number and profiles in one window. The tool applies each profile, builds it, and puts your project settings back afterwards.

This removes repetitive manual work, reduces mistakes, and makes managing multiple environments or configurations significantly easier.

Supported targets: **Android** (APK, split APKs, AAB, exported Gradle project) and **iOS** (Xcode project).
Supports Unity 2022.3 and Unity 6.

---

## **How to Open**
**Tools → Easy Builder**

---

## **Features**
- One build per profile, in one click
- Settings are checked before anything is built, so a batch doesn't fail halfway through
- **Automatic revert:** package name, product name, icons, AAB/split settings and keystore are restored after every build, including failed and canceled ones
- Set **Version** and **Build Number** from the window
- Per-profile scene list and extra scripting define symbols
- Profile-based keystore selection, with passwords kept out of the asset
- Addressables content build and EDM4U Android dependency resolution, detected automatically
- A summary at the end, with a button to show the builds in their folder
- Command-line entry point for CI

---

## **Profiles**
Profiles are `ScriptableObject` assets. Create one with **Create → Easy Builder → New Base Profile**.

### **Base profile settings**
- **Package Name**: application identifier (leave empty to keep the project's)
- **AAB Build** / **Split Architectures** (Android)
- **Override Title**: product name for this profile
- **Override Icon** (Android): adaptive, round and legacy icons. Every density is replaced; a texture left empty keeps the project's icon of that kind
- **Scenes**: scenes for this profile (leave empty to use the scenes enabled in Build Settings)
- **Extra Scripting Defines**: define symbols added for this profile's build only

### **Custom profiles**
Inherit from `BaseBuildProfileSO` (namespace `EasyBuilder`) to add your own settings:

```csharp
using EasyBuilder;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(menuName = "Easy Builder/My Profile")]
public class MyProfileSO : BaseBuildProfileSO
{
    public override void ApplyProfile()
    {
        base.ApplyProfile(); // call first: it records the settings to restore
        // apply your changes
    }

    public override void RevertProfile()
    {
        // undo your changes
        base.RevertProfile();
    }

    // Optional: change the build options, e.g. options.options |= BuildOptions.CompressWithLz4;
    public override void ConfigureBuildOptions(ref BuildPlayerOptions options) => base.ConfigureBuildOptions(ref options);
}
```

You can also override `Validate(BuildTarget, List<string>)` to report problems before the batch starts.
If your profile scripts live in their own assembly definition, add a reference to `EasyBuilder.Editor`.

---

## **Multi-Profile Build**
The window builds every selected profile in order:
1. Apply the profile
2. Build it
3. Revert the profile's changes

The batch stops at the first failed or canceled build. All builds go into the folder you pick, named
`<package>_v<version>_<build number>_<profile name>` (`.apk` or `.aab` for single-file Android builds; split APKs, exported Gradle projects and Xcode projects are folders).

The profile selection is saved per user in `UserSettings/EasyBuilderSettings.asset`, which Unity's default `.gitignore` excludes.

---

## **Keystore**
Create a keystore profile with **Create → Easy Builder → New Keystore Profile** and select it in the window.
The asset stores only the keystore path and alias. Passwords are read from, in order:
1. The environment variables named in the profile (`EASYBUILDER_KEYSTORE_PASS` and `EASYBUILDER_KEYALIAS_PASS` by default), for CI
2. The passwords entered in the profile's inspector, which are saved in this machine's EditorPrefs

If no keystore profile is selected, the keystore set in Player Settings is used unchanged.
Profiles from v1.x that still hold passwords in the asset keep working, and the inspector offers to move the passwords out.

---

## **Command Line / CI**
```
Unity -batchmode -quit -projectPath . -buildTarget Android ^
      -executeMethod EasyBuilder.BuildRunner.BuildFromCommandLine ^
      -ebProfiles "Assets/Profiles/Store.asset;Assets/Profiles/Demo.asset" ^
      -ebKeystore Assets/Profiles/Release.asset ^
      -ebOutput Builds
```
| Argument | |
|---|---|
| `-ebOutput <folder>` | Required. Where builds are written. |
| `-ebProfiles "<a>;<b>"` | Profile asset paths or GUIDs. Defaults to the window's selection. |
| `-ebKeystore <path or GUID>` | Keystore profile. Defaults to the window's selection. |
| `-ebVersion <version>` | Sets the version before building. |
| `-ebBuildNumber <number>` | Sets the build number before building. |
| `-ebSkipDependencyResolve` | Skips the EDM4U Android dependency resolve. |

Unity exits with code 0 when every profile built and 1 otherwise.

---

## **Addressables and EDM4U**
- **Addressables** (1.19.11 or newer) are detected automatically, so no define symbol is needed. Content is built before each profile, and *Build Addressables on Player Build* is switched off during the batch so it isn't built twice.
- **External Dependency Manager (EDM4U)** is detected automatically. Android dependencies are force-resolved before each Android build; turn this off in the window if you don't need it.

The `ADDRESSABLE_ACTIVE` and `GOOGLE_PLAY_ACTIVE` define symbols from v1.x are no longer needed.

---

## **Upgrading from v1.x**
- The classes are now in the `EasyBuilder` namespace; add `using EasyBuilder;` to your custom profiles.
- `RevertProfile` is now virtual. Override it to undo your own changes.
- Your profile selection is imported from `Assets/Plugins/buildProfile.txt` the first time you open the window. You can delete that file afterwards.
- Easy Builder no longer turns off the custom keystore when no keystore profile is selected.

---
