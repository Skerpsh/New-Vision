// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Adds a Wi-Fi network from a QR code. Android 11+ doesn't let apps join networks silently: the system shows its
    /// "Save network?" dialog (Settings.ACTION_WIFI_ADD_NETWORKS) and the user confirms. Falls back to the Wi-Fi settings.
    /// </summary>
    public static class WifiConnector
    {
        public enum Outcome { DialogShown, SettingsOpened, Failed }

        public static Outcome Connect(CodeContent wifi, out string message)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var builder = new AndroidJavaObject("android.net.wifi.WifiNetworkSuggestion$Builder");
                Chain(builder.Call<AndroidJavaObject>("setSsid", wifi.WifiSsid));
                var security = wifi.WifiSecurity ?? "";
                if (security == "SAE" || security == "WPA3")
                {
                    Chain(builder.Call<AndroidJavaObject>("setWpa3Passphrase", wifi.WifiPassword ?? ""));
                }
                else if (security.StartsWith("WPA"))
                {
                    Chain(builder.Call<AndroidJavaObject>("setWpa2Passphrase", wifi.WifiPassword ?? ""));
                }
                else if (security.Length > 0)
                {
                    // WEP and enterprise networks can't be suggested by apps
                    throw new NotSupportedException($"{security} networks can't be added by apps");
                }
                if (wifi.WifiHidden)
                {
                    Chain(builder.Call<AndroidJavaObject>("setIsHiddenSsid", true));
                }
                using var suggestion = builder.Call<AndroidJavaObject>("build");
                using var list = new AndroidJavaObject("java.util.ArrayList");
                _ = list.Call<bool>("add", suggestion);
                using var intent = new AndroidJavaObject("android.content.Intent", "android.settings.WIFI_ADD_NETWORKS");
                Chain(intent.Call<AndroidJavaObject>("putParcelableArrayListExtra", "android.provider.extra.WIFI_NETWORK_LIST", list));
                StartActivity(intent);
                message = "Confirm in the system dialog";
                return Outcome.DialogShown;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] Wi-Fi add dialog failed: {e.Message}");
            }
            try
            {
                using var intent = new AndroidJavaObject("android.content.Intent", "android.settings.WIFI_SETTINGS");
                StartActivity(intent);
                message = "Pick the network in Wi-Fi settings and enter the password shown here";
                return Outcome.SettingsOpened;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] Wi-Fi settings failed: {e.Message}");
            }
#endif
            message = "Can't open Wi-Fi settings, connect manually with the password shown here";
            return Outcome.Failed;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Builder methods return the builder itself; the returned reference only needs releasing
        private static void Chain(AndroidJavaObject result) => result?.Dispose();

        private static void StartActivity(AndroidJavaObject intent)
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
            activity.Call("startActivity", intent);
        }
#endif
    }
}
