// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using UnityEngine;

namespace PassthroughCameraSamples.MultiObjectDetection
{
    /// <summary>
    /// Which object classes the detector reports. Changed from the wrist panel (GENERAL > CLASSES); saved between launches.
    /// </summary>
    public class ClassFilter
    {
        private const string PrefsKey = "NewVision.EnabledClasses";

        private static readonly string[] s_home =
        {
            "Chair", "Couch", "Potted plant", "Bed", "Dining table", "Toilet", "TV", "Laptop",
            "Refrigerator", "Sink", "Oven", "Microwave", "Clock", "Vase"
        };

        public string[] Labels { get; }

        /// <summary>Enabled flag per class id.</summary>
        public bool[] EnabledClasses { get; }

        /// <summary>Raised whenever the selection changes.</summary>
        public event Action Changed;

        public int EnabledCount
        {
            get
            {
                var count = 0;
                foreach (var enabled in EnabledClasses)
                {
                    if (enabled)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public ClassFilter(TextAsset labelsAsset)
        {
            Labels = labelsAsset.text.Split('\n');
            for (var i = 0; i < Labels.Length; i++)
            {
                Labels[i] = Labels[i].Trim();
            }
            EnabledClasses = new bool[Labels.Length];
            Load();
        }

        public void Toggle(int classId)
        {
            EnabledClasses[classId] = !EnabledClasses[classId];
            Commit();
        }

        public void SetAll(bool value)
        {
            for (var i = 0; i < EnabledClasses.Length; i++)
            {
                EnabledClasses[i] = value;
            }
            Commit();
        }

        /// <summary>Only furniture, appliances and plants: a calm view at home.</summary>
        public void HomePreset()
        {
            for (var i = 0; i < Labels.Length; i++)
            {
                EnabledClasses[i] = Array.IndexOf(s_home, Labels[i]) >= 0;
            }
            Commit();
        }

        private void Commit()
        {
            Save();
            Changed?.Invoke();
        }

        private void Load()
        {
            var saved = PlayerPrefs.GetString(PrefsKey, "");
            for (var i = 0; i < EnabledClasses.Length; i++)
            {
                EnabledClasses[i] = saved.Length != EnabledClasses.Length || saved[i] == '1';
            }
        }

        private void Save()
        {
            var chars = new char[EnabledClasses.Length];
            for (var i = 0; i < chars.Length; i++)
            {
                chars[i] = EnabledClasses[i] ? '1' : '0';
            }
            PlayerPrefs.SetString(PrefsKey, new string(chars));
            PlayerPrefs.Save();
        }
    }
}
