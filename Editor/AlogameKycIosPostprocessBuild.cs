#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEditor.iOS.Xcode.Extensions;
using UnityEngine;

namespace Alogame.KycSdk.Editor
{
    /// <summary>
    /// Configures the generated Xcode project so a game developer never opens it.
    ///
    /// Three things Unity does not do on its own, each one a real failure this
    /// repo has already hit while integrating the same xcframework into Cocos:
    ///
    ///  1. SWIFT_VERSION — the bridge is a .swift file (it has to be: the iOS SDK
    ///     exposes no @objc surface, so Objective-C++ cannot see it). No Unity
    ///     template target uses Swift, so this is never set, and the file fails
    ///     to compile with "Swift language version not specified".
    ///
    ///  2. LD_RUNPATH_SEARCH_PATHS — AlogameKycKit is a *dynamic* framework.
    ///     Embedding it is necessary but not sufficient: dyld resolves
    ///     @rpath/AlogameKycKit.framework/AlogameKycKit against this setting, and
    ///     without it the app builds cleanly and then dies at launch with
    ///     "Library not loaded: @rpath/AlogameKycKit.framework/AlogameKycKit".
    ///
    ///  3. Embed + code-sign on copy — a dynamic framework that is linked but not
    ///     embedded is missing from the .app bundle entirely.
    ///
    /// Unity 2019.3+ generates two targets. Plugins compile into UnityFramework;
    /// the .app is the main target, and only the main target can embed. Getting
    /// this backwards produces exactly the launch crash in (2), so both targets
    /// are configured explicitly below rather than assuming which one applies.
    /// </summary>
    public static class AlogameKycIosPostprocessBuild
    {
        private const string FrameworkName = "AlogameKycKit.framework";
        private const string Tag = "[AlogameKycSdk]";

        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            var projectPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var project = new PBXProject();
            project.ReadFromFile(projectPath);

            var mainTarget = project.GetUnityMainTargetGuid();
            var frameworkTarget = project.GetUnityFrameworkTargetGuid();

            // Swift needs a language version on whichever target compiles the
            // bridge. Set on both: which target a Plugins/iOS source lands in has
            // changed across Unity versions, and setting it on a target that has
            // no Swift file is harmless.
            //
            // @executable_path/Frameworks is the entry that matters, and it is
            // needed on the *framework* target specifically, not just the app:
            // verified on a real build, the binary carrying the
            // @rpath/AlogameKycKit.framework/AlogameKycKit load command is
            // UnityFramework, not the app executable. @executable_path is still
            // the right anchor there — it resolves against the .app regardless of
            // which binary does the loading — whereas @loader_path would resolve
            // inside UnityFramework.framework itself and find nothing.
            foreach (var t in new[] { mainTarget, frameworkTarget })
            {
                project.SetBuildProperty(t, "SWIFT_VERSION", "5.0");
                project.AddBuildProperty(t, "LD_RUNPATH_SEARCH_PATHS", "@executable_path/Frameworks");
            }

            EmbedFramework(project, mainTarget, pathToBuiltProject);

            project.WriteToFile(projectPath);
        }

        /// <summary>
        /// Unity copies the .xcframework into the generated project and adds it to
        /// the *link* phase itself, but does not embed it. A dynamic framework that
        /// is linked and not embedded is absent from the .app at runtime, so the
        /// embed below is the part that matters.
        ///
        /// The search deliberately picks a slice rather than the .xcframework
        /// directory: an embed build phase copies what it is given verbatim, and
        /// handing it the umbrella .xcframework would put both slices — including
        /// the simulator one — inside the shipped bundle, which App Store
        /// validation rejects.
        ///
        /// Slice choice is explicit for the same reason it was a bug when it was
        /// not: a real export showed Directory.GetDirectories returning both
        /// ios-arm64 and ios-arm64_x86_64-simulator, in filesystem order, so
        /// "take the first" happened to be right only by luck.
        /// </summary>
        private static void EmbedFramework(PBXProject project, string mainTarget, string pathToBuiltProject)
        {
            var wantSimulator = PlayerSettings.iOS.sdkVersion == iOSSdkVersion.SimulatorSDK;

            var candidates = Directory.GetDirectories(pathToBuiltProject, FrameworkName, SearchOption.AllDirectories);
            if (candidates.Length == 0)
            {
                Debug.LogError(Tag + " " + FrameworkName + " not found in the generated Xcode project. " +
                               "The app will crash at launch with a missing-dylib error. Check that " +
                               "Runtime/Plugins/iOS/AlogameKycKit.xcframework is included in the build.");
                return;
            }

            string chosen = null;
            foreach (var candidate in candidates)
            {
                // Slice directory names are "ios-arm64" and "ios-arm64_x86_64-simulator".
                var isSimulator = candidate.Contains("-simulator");
                if (isSimulator == wantSimulator) { chosen = candidate; break; }
            }
            if (chosen == null)
            {
                Debug.LogError(Tag + " no " + (wantSimulator ? "simulator" : "device") +
                               " slice found inside AlogameKycKit.xcframework (looked at " +
                               candidates.Length + " candidate(s)).");
                return;
            }

            // PBXProject paths are relative to the generated project root.
            var relative = chosen.Substring(pathToBuiltProject.Length).TrimStart('/', '\\');
            var fileGuid = project.AddFile(chosen, relative, PBXSourceTree.Absolute);
            project.AddFileToEmbedFrameworks(mainTarget, fileGuid);

            var frameworkDir = Path.GetDirectoryName(relative);
            if (!string.IsNullOrEmpty(frameworkDir))
            {
                project.AddBuildProperty(mainTarget, "FRAMEWORK_SEARCH_PATHS", "$(PROJECT_DIR)/" + frameworkDir);
            }

            Debug.Log(Tag + " embedded the " + (wantSimulator ? "simulator" : "device") +
                      " slice (" + relative + ") and configured rpath.");
        }
    }
}
#endif
