using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Facet.EditorTools
{
    /// <summary>
    /// The WebGL build, in one command, because the assignment ships as a WebGL build on GitHub Pages
    /// and a build that is only reproducible by hand in the Editor is a build that breaks quietly.
    ///
    /// One setting it fixes on the way: GitHub Pages does not send Content-Encoding, so a
    /// Brotli-compressed payload arrives unusable. Unity's decompression fallback is enabled so the
    /// loader decompresses in JavaScript when the host cannot.
    ///
    /// The vertex-colour shader needs no such handling: the Palette's view material is a real asset
    /// referencing it, and the scene references the Palette, so the build carries the shader through
    /// that chain (guarded by ProjectTests).
    ///
    /// Two ways in, one implementation: <b>FACET &gt; Build WebGL</b> in the Editor, and batchmode
    /// (<c>Unity -batchmode -quit -projectPath &lt;project&gt; -executeMethod Facet.EditorTools.BuildWebGL.Build</c>),
    /// which is what Tools/build-webgl.sh runs and the path the build actually ships through. A
    /// failure has to read differently to each of them - see Fail.
    /// </summary>
    public static class BuildWebGL
    {
        private const string ScenePath = "Assets/Scenes/Main.unity";

        /// <summary>Where the build lands. Git-ignored: it is an artefact, and Pages serves a copy.</summary>
        private const string OutputDirectory = "Builds/WebGL";

        /// <summary>A build that cannot be published is not finished, so a batchmode run exits non-zero.</summary>
        private const int FailureExitCode = 1;

        /// <summary>What a failed menu build says, since a menu build has no exit code to fail with.</summary>
        private const string FailureTitle = "FACET: WebGL build failed";

        [MenuItem("FACET/Build WebGL")]
        public static void Build()
        {
            try
            {
                ConfigureForStaticHosting();

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = OutputDirectory,
                    target = BuildTarget.WebGL,
                    options = BuildOptions.None,
                };

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                if (summary.result != BuildResult.Succeeded)
                {
                    Fail("FACET: WebGL build " + summary.result + " with " + summary.totalErrors + " errors.");
                    return;
                }

                // Pages refuses to serve paths starting with an underscore unless this file is there,
                // and the WebGL template ships a folder called Build/.
                File.WriteAllText(Path.Combine(OutputDirectory, ".nojekyll"), string.Empty);

                Debug.Log("FACET: WebGL build at " + Path.GetFullPath(OutputDirectory) +
                    " (" + (summary.totalSize / (1024 * 1024)) + " MB). Publish that folder as it stands.");
            }
            catch (Exception error)
            {
                Fail("FACET: WebGL build threw: " + error);
            }
        }

        /// <summary>
        /// Reports a build that produced no player, in whichever way the caller can receive it.
        ///
        /// One method serves two callers that want opposite things. Batchmode wants a non-zero exit
        /// code and gets one. The menu wants anything but: EditorApplication.Exit quits the Editor on
        /// the spot and does not offer to save, so a build that failed after the scene was edited -
        /// which is exactly when a build is run from the menu - would take that edit down with it.
        /// The menu gets a dialog instead, and both callers get the log line.
        /// </summary>
        private static void Fail(string message)
        {
            Debug.LogError(message);

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(FailureExitCode);
                return;
            }

            EditorUtility.DisplayDialog(FailureTitle, message, "OK");
        }

        /// <summary>Compression settings a plain static host can actually serve.</summary>
        private static void ConfigureForStaticHosting()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
        }
    }
}
