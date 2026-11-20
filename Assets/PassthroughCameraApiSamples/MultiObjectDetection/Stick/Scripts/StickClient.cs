// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Collections.Generic;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Talks to the M5StickC Plus2 on the headset over Bluetooth LE (one text line per message, see M5Stick/src/main.cpp).
    /// The Java side (Plugins/Android/NewVisionBleLink.java) scans for "New Vision Stick", connects without pairing and
    /// reassembles lines. Here: permissions, "NV?" handshake, "STATUS" poll every second, commands.
    /// The stick forwards laptop commands as keyboard shortcuts (it is the laptop's Bluetooth keyboard at the same time).
    /// </summary>
    public class StickClient : MonoBehaviour
    {
        private const string AddressKey = "NewVision.StickBleAddress";
        private static readonly string[] Permissions = { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT" };
        private const float TickInterval = 0.5f;
        private const float StatusInterval = 1f;
        private const float HelloInterval = 1.5f;
        private const float LinkTimeout = 3.5f;
        private const int MaxLinesPerFrame = 32;

        private AndroidJavaObject m_serial;
        private float m_nextTick;
        private float m_nextStatus;
        private float m_nextHello;
        private float m_lastReplyTime = -100f;
        private float m_statusSentTime = -1f;
        private float m_nextTimeSync;
        private string m_portState = "starting";

        /// <summary>The stick answers over USB.</summary>
        public bool Connected => Time.time - m_lastReplyTime < LinkTimeout;
        /// <summary>BLE side: "no permission", "bluetooth off", "scanning", "connecting", "open", "error: ...", or "editor".</summary>
        public string PortState => m_portState;
        /// <summary>Round trip of the last STATUS poll, ms.</summary>
        public int PingMs { get; private set; } = -1;
        /// <summary>Last STATUS fields: bt, bat, chg, vol, bri, scr, up, heap.</summary>
        public IReadOnlyDictionary<string, string> Status => m_status;
        private readonly Dictionary<string, string> m_status = new();

        public bool LaptopPaired => Connected && Field("bt") == "1";

        /// <summary>Every reply except STATUS ("OK play", "ERR notpaired", ...).</summary>
        public event Action<string> Reply;

        public string Field(string key) => m_status.TryGetValue(key, out var value) ? value : null;
        /// <summary>Updates a field right away after a SET, so quick repeated presses build on it before the next STATUS.</summary>
        public void SetField(string key, string value) => m_status[key] = value;
        public int IntField(string key, int fallback = 0) => int.TryParse(Field(key), out var v) ? v : fallback;

        private void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                m_serial = new AndroidJavaObject("com.newvision.ble.NewVisionBleLink", activity);
                m_serial.Call("setLastAddress", PlayerPrefs.GetString(AddressKey, ""));
            }
            catch (Exception e)
            {
                m_portState = "error: " + e.Message;
                Debug.LogWarning($"[NewVision] stick: BLE plugin failed: {e.Message}");
            }
            RequestPermissions();
#else
            m_portState = "editor";
#endif
        }

        private void OnDestroy()
        {
            if (m_serial != null)
            {
                m_serial.Call("close");
                m_serial.Dispose();
                m_serial = null;
            }
        }

        private void Update()
        {
            if (m_serial == null)
            {
                return;
            }

            if (Time.time >= m_nextTick)
            {
                m_nextTick = Time.time + TickInterval;
                m_serial.Call("tick");
                var state = m_serial.Call<string>("getState");
                if (state != m_portState)
                {
                    var address = m_serial.Call<string>("getDeviceName");
                    Debug.Log($"[NewVision] stick BLE: {state} {address}");
                    m_portState = state;
                    m_nextHello = 0f;
                    if (state == "open" && !string.IsNullOrEmpty(address))
                    {
                        PlayerPrefs.SetString(AddressKey, address);
                        PlayerPrefs.Save();
                    }
                }
            }

            for (var i = 0; i < MaxLinesPerFrame; i++)
            {
                var line = m_serial.Call<string>("readLine");
                if (line == null)
                {
                    break;
                }
                Handle(line);
            }

            if (m_portState != "open")
            {
                return;
            }
            if (!Connected && Time.time >= m_nextHello)
            {
                m_nextHello = Time.time + HelloInterval;
                Write("NV?");
            }
            if (Connected && Time.time >= m_nextStatus)
            {
                m_nextStatus = Time.time + StatusInterval;
                m_statusSentTime = Time.realtimeSinceStartup;
                Write("STATUS");
            }
        }

        private void Handle(string text)
        {
            // "# ..." = the stick's own debug log; anything else unknown = boot noise
            if (text.StartsWith("#"))
            {
                Debug.Log($"[NewVision] stick log: {text.Substring(1).Trim()}");
                return;
            }
            if (text.StartsWith("NVSTICK"))
            {
                if (!Connected)
                {
                    Debug.Log("[NewVision] stick connected over Bluetooth");
                }
                m_lastReplyTime = Time.time;
                m_nextStatus = 0f;
                return;
            }
            if (text.StartsWith("STATUS"))
            {
                m_lastReplyTime = Time.time;
                if (m_statusSentTime >= 0f)
                {
                    PingMs = Mathf.RoundToInt((Time.realtimeSinceStartup - m_statusSentTime) * 1000f);
                    m_statusSentTime = -1f;
                }
                foreach (var part in text.Split(' '))
                {
                    var eq = part.IndexOf('=');
                    if (eq > 0)
                    {
                        m_status[part.Substring(0, eq)] = part.Substring(eq + 1);
                    }
                }
                // The stick has no clock of its own: it shows the headset's time (after a reboot it asks again via clk=0)
                if (Field("clk") == "0" && Time.time >= m_nextTimeSync)
                {
                    m_nextTimeSync = Time.time + 5f;
                    var now = DateTimeOffset.Now;
                    _ = Write($"TIME {now.ToUnixTimeSeconds()} {(int)now.Offset.TotalMinutes}");
                }
                return;
            }
            if (text.StartsWith("OK") || text.StartsWith("ERR"))
            {
                m_lastReplyTime = Time.time;
                Debug.Log($"[NewVision] stick: {text}");
                Reply?.Invoke(text);
            }
        }

        /// <summary>Sends a protocol line to the stick ("CMD play", "SET vol 64", "BEEP", ...).</summary>
        public bool Send(string line)
        {
            if (m_serial == null || m_portState != "open")
            {
                return false;
            }
            Debug.Log($"[NewVision] stick <- {line}");
            return Write(line);
        }

        /// <summary>Asks for "Nearby devices" (Bluetooth scan + connect); without it the stick can't be found.</summary>
        public void RequestPermissions()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            foreach (var permission in Permissions)
            {
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission))
                {
                    UnityEngine.Android.Permission.RequestUserPermissions(Permissions);
                    return;
                }
            }
#endif
        }

        /// <summary>Drop the link state and say hello again (after the stick rebooted).</summary>
        public void Reconnect()
        {
            if (m_portState == "no permission")
            {
                RequestPermissions();
            }
            m_lastReplyTime = -100f;
            m_nextHello = 0f;
            m_status.Clear();
            PingMs = -1;
        }

        private bool Write(string line) => m_serial != null && m_serial.Call<bool>("writeLine", line);
    }
}
