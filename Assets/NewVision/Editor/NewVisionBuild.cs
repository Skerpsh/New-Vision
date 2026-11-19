// Copyright (c) 2026 New Vision contributors
// SPDX-License-Identifier: AGPL-3.0-only

using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace NewVision.Editor
{
    /// <summary>
    /// Command line build: Unity.exe -batchmode -quit -projectPath ... -buildTarget Android
    ///   -executeMethod NewVision.Editor.NewVisionBuild.BuildAndroid [-output Builds/NewVision.apk]
    /// Builds the scenes enabled in Build Settings, same as File > Build.
    /// </summary>
    public static class NewVisionBuild
    {
        public static void BuildAndroid()
        {
            var args = Environment.GetCommandLineArgs();
            var outIndex = Array.IndexOf(args, "-output");
            var output = outIndex >= 0 && outIndex + 1 < args.Length ? args[outIndex + 1] : "Builds/NewVision.apk";

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            EditorUserBuildSettings.buildAppBundle = false;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });
            Debug.Log($"[NewVisionBuild] {report.summary.result}: {output}, {report.summary.totalSize / (1024 * 1024)} MB, " +
                      $"{report.summary.totalTime.TotalSeconds:0} s, errors {report.summary.totalErrors}");
            if (report.summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
