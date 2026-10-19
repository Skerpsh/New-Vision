// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Adds a Wi-Fi network from a QR code. Android 10+ doesn't let apps join networks silently. Horizon OS ignores the
    /// "Save network?" dialog (ACTION_WIFI_ADD_NETWORKS just opens Settings), so the network is handed to the system as a
    /// suggestion (WifiManager.addNetworkSuggestions): the headset joins it by itself when it is in range, after the user
    /// allows it once. Wi-Fi settings with the password on the clipboard are the fallback.
    /// </summary>
    public static class WifiConnector
    {
        // WifiManager.STATUS_NETWORK_SUGGESTIONS_*
        private const int StatusSuccess = 0;
        private const int StatusDuplicate = 3;

        /// <summary>Suggests the network to the system. Returns false when that isn't possible (then use <see cref="OpenSettings"/>).</summary>
        public static bool Suggest(CodeContent wifi, out string message)
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
                    message = $"{security} networks can't be added by apps";
                    return false;
                }
                if (wifi.WifiHidden)
                {
                    Chain(builder.Call<AndroidJavaObject>("setIsHiddenSsid", true));
                }
                using var suggestion = builder.Call<AndroidJavaObject>("build");
                using var list = new AndroidJavaObject("java.util.ArrayList");
                _ = list.Call<bool>("add", suggestion);

                using var activity = CurrentActivity();
                using var wifiManager = activity.Call<AndroidJavaObject>("getSystemService", "wifi");
                var status = wifiManager.Call<int>("addNetworkSuggestions", list);
                Debug.Log($"[NewVision] Wi-Fi suggestion status {status}");
                if (status == StatusSuccess || status == StatusDuplicate)
                {
                    message = "Saved. The headset joins it by itself when it's in range. If a notification asks, allow it";
                    return true;
                }
                message = $"The system refused the network (status {status})";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] Wi-Fi suggestion failed: {e.Message}");
                message = "Can't add the network from the app";
            }
            return false;
#else
            message = "Only works on the headset";
            return false;
#endif
        }

        /// <summary>Opens Wi-Fi settings with the password on the clipboard, ready to paste.</summary>
        public static string OpenSettings(CodeContent wifi)
        {
            var copied = false;
            if (!string.IsNullOrEmpty(wifi.WifiPassword))
            {
                GUIUtility.systemCopyBuffer = wifi.WifiPassword;
                copied = true;
            }
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var intent = new AndroidJavaObject("android.content.Intent", "android.settings.WIFI_SETTINGS");
                using var activity = CurrentActivity();
                activity.Call("startActivity", intent);
                return copied ? "Password copied: pick the network in Settings and paste it" : "Pick the network in Settings";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[NewVision] Wi-Fi settings failed: {e.Message}");
            }
#endif
            return copied ? "Password copied, connect in Settings > Wi-Fi" : "Connect in Settings > Wi-Fi";
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // Builder methods return the builder itself; the returned reference only needs releasing
        private static void Chain(AndroidJavaObject result) => result?.Dispose();

        private static AndroidJavaObject CurrentActivity()
        {
            using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            return player.GetStatic<AndroidJavaObject>("currentActivity");
        }
#endif
    }
}
