using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace NewVision.Editor
{
    /// <summary>
    /// Menu "New Vision > Apply App Settings": sets the app name, package id and icon in Player Settings.
    /// </summary>
    public static class NewVisionAppSettings
    {
        private const string ProductName = "New Vision";
        private const string CompanyName = "New Vision";
        private const string PackageId = "com.newvision.app";
        private const string IconPath = "Assets/NewVision/AppIcon.png";

        [MenuItem("New Vision/Apply App Settings")]
        public static void Apply()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, PackageId);

            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogError($"[NewVision] Icon not found at {IconPath}");
            }
            else
            {
                // Default icon: used by every platform that has no icon of its own (Android included)
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[NewVision] App settings applied: name '{ProductName}', package '{PackageId}', icon '{IconPath}'");
        }
    }
}
