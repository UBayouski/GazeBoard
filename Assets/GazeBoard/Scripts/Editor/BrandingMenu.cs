using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace GazeBoard.EditorTools
{
    /// <summary>
    /// Applies the app icon and the launch splash. Both images are rendered outside Unity from
    /// the same mark and colours as the Calm theme (hackathon_package/app_icon) and copied into
    /// Assets/GazeBoard/Branding; this command only points the project settings at them.
    /// </summary>
    public static class BrandingMenu
    {
        private const string IconPath = "Assets/GazeBoard/Branding/AppIcon.png";
        private const string SplashPath = "Assets/GazeBoard/Branding/SystemSplash.png";

        [MenuItem("GazeBoard/Apply App Icon and Splash")]
        private static void Apply()
        {
            Texture2D icon = Import(IconPath, transparent: false);
            Texture2D splash = Import(SplashPath, transparent: true);
            if (icon == null || splash == null)
            {
                EditorUtility.DisplayDialog("GazeBoard", $"Missing {IconPath} or {SplashPath}.", "OK");
                return;
            }

            // The default icon is used for every platform and icon kind that has none of its own.
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);

            // The headset's own launch splash: shown by the operating system the moment the app
            // is started, before Unity has loaded, over passthrough or black.
            OVRProjectConfig config = OVRProjectConfig.CachedProjectConfig;
            config.systemSplashScreen = splash;
            config.systemSplashScreenType = OVRProjectConfig.SystemSplashScreenType.Mono;
            OVRProjectConfig.CommitProjectConfig(config);

            // Unity's own splash would follow it as a second, different-looking screen. Turn it
            // off where the licence allows; where it does not, at least match the dark ground.
            PlayerSettings.SplashScreen.backgroundColor = new Color(0.051f, 0.059f, 0.075f, 1f);
            PlayerSettings.SplashScreen.unityLogoStyle = PlayerSettings.SplashScreen.UnityLogoStyle.LightOnDark;
            PlayerSettings.SplashScreen.show = false;

            AssetDatabase.SaveAssets();
            Debug.Log("[GazeBoard] App icon and launch splash applied. " +
                      $"Unity splash screen is now {(PlayerSettings.SplashScreen.show ? "still ON (licence requires it)" : "off")}.");
        }

        private static Texture2D Import(string path, bool transparent)
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.alphaIsTransparency = transparent;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
