using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// Stops a player build that is not on the runtime the determinism work was verified
// on (decision D5, 2026-10-02: the study build is Windows 64-bit, Mono scripting
// backend, managed stripping off).
//
// Why it matters: the simulation is proven bit-identical between Unity's Mono and
// .NET (Results/2026-10-02_determinism). IL2CPP compiles the same C# through a C++
// compiler, where float behaviour depends on compiler flags, and code stripping can
// silently remove the fields the replay and telemetry JSON is built from. A study
// session recorded by such a build might not replay, and nobody would know until
// the data was needed.
//
// To build for another target on purpose (a demo, never the study), add the
// scripting define THESIS_ALLOW_UNVERIFIED_RUNTIME in Player Settings.
public class BuildGuard : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
#if !THESIS_ALLOW_UNVERIFIED_RUNTIME
        NamedBuildTarget target = NamedBuildTarget.FromBuildTargetGroup(report.summary.platformGroup);
        ScriptingImplementation backend = PlayerSettings.GetScriptingBackend(target);
        ManagedStrippingLevel stripping = PlayerSettings.GetManagedStrippingLevel(target);

        if (backend != ScriptingImplementation.Mono2x || stripping != ManagedStrippingLevel.Disabled)
        {
            throw new BuildFailedException(
                "[BuildGuard] This build would use scripting backend " + backend + " with managed stripping " + stripping + " on " + report.summary.platform
                + ". The study build must be Mono with stripping Disabled (Docs/ARCHITECTURE.md §0, D5): that is the runtime the replays were verified on."
                + " Change it in Player Settings > Other Settings, or define THESIS_ALLOW_UNVERIFIED_RUNTIME for a build that will never record study data.");
        }
#endif
    }
}
