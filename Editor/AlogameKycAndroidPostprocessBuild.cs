#if UNITY_ANDROID
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor.Android;
using UnityEngine;

namespace Alogame.KycSdk.Editor
{
    /// <summary>
    /// Adds the Kotlin standard library to the generated Gradle project.
    ///
    /// Why this is necessary: alogame-kyc-sdk.aar is vendored as a plain file,
    /// not resolved from a Maven coordinate, so it carries no POM and Gradle has
    /// no way to learn about its transitive dependencies. The SDK is Kotlin, and
    /// its enums compile against kotlin.enums.EnumEntriesKt (Kotlin 1.9+), so
    /// without the stdlib on the runtime classpath the very first call dies:
    ///
    ///     java.lang.NoClassDefFoundError:
    ///       Failed resolution of: Lkotlin/enums/EnumEntriesKt;
    ///       at vn.alogame.kycsdk.AlogameKycEnv.&lt;clinit&gt;
    ///
    /// Found on a real device, not in review — the APK builds and installs
    /// perfectly without this.
    ///
    /// Declared as a Gradle dependency rather than shipping kotlin-stdlib.jar in
    /// Plugins/Android on purpose: a raw jar collides with the host game's own
    /// Kotlin (duplicate classes at dex-merge time) in any project that already
    /// uses it, whereas a coordinate lets Gradle resolve one version for
    /// everyone. Appending is additive — no existing configuration is rewritten,
    /// which is the constraint a plugin sharing a project with others has to
    /// respect.
    /// </summary>
    public sealed class AlogameKycAndroidPostprocessBuild : IPostGenerateGradleAndroidProject
    {
        private const string Tag = "[AlogameKycSdk]";

        // Pinned to the version the AAR itself was built with. AGP 9 ships
        // built-in Kotlin support, so this only has to satisfy the runtime
        // classpath, not configure a Kotlin compiler.
        private const string Coordinate = "org.jetbrains.kotlin:kotlin-stdlib:2.2.10";

        // Runs after Unity's own Gradle generation; ordering against other
        // plugins does not matter because this only appends.
        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var buildGradle = Path.Combine(path, "build.gradle");
            if (!File.Exists(buildGradle))
            {
                // Unity 2023+/6 can emit Kotlin-DSL build files instead.
                buildGradle = Path.Combine(path, "build.gradle.kts");
                if (!File.Exists(buildGradle))
                {
                    Debug.LogError(Tag + " no build.gradle in " + path +
                                   " — could not add the Kotlin stdlib, and the SDK will throw " +
                                   "NoClassDefFoundError on its first call.");
                    return;
                }
            }

            var text = File.ReadAllText(buildGradle);
            if (text.Contains("kotlin-stdlib"))
            {
                Debug.Log(Tag + " kotlin-stdlib already declared — nothing to do.");
                return;
            }

            var isKts = buildGradle.EndsWith(".kts");
            var line = isKts
                ? "    implementation(\"" + Coordinate + "\")"
                : "    implementation '" + Coordinate + "'";

            // Match the first `dependencies {` at the start of a line so a
            // `dependencies` inside buildscript{} or a comment is not hit.
            var match = Regex.Match(text, @"(?m)^dependencies\s*\{");
            if (match.Success)
            {
                text = text.Insert(match.Index + match.Length, "\n" + line);
            }
            else
            {
                text += "\n\ndependencies {\n" + line + "\n}\n";
            }

            File.WriteAllText(buildGradle, text);
            Debug.Log(Tag + " added " + Coordinate + " to " + Path.GetFileName(buildGradle));
        }
    }
}
#endif
