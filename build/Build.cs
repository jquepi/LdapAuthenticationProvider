using Nuke.Common;
using Nuke.Common.Execution;
using Nuke.Common.IO;
using Nuke.Common.ProjectModel;
using Nuke.Common.Tools.DotNet;
using Nuke.Common.Tools.OctoVersion;
using Nuke.Common.Utilities.Collections;
using static Nuke.Common.IO.FileSystemTasks;
using static Nuke.Common.Tools.DotNet.DotNetTasks;

[CheckBuildProjectConfigurations]
[UnsetVisualStudioEnvironmentVariables]
class Build : NukeBuild
{
    readonly Configuration Configuration = Configuration.Release;

    [Solution] readonly Solution Solution;

    [Parameter] readonly bool? OctoVersionAutoDetectBranch = NukeBuild.IsLocalBuild;
    [Parameter] readonly string OctoVersionBranch;
    [Parameter] readonly int? OctoVersionFullSemVer;
    [Parameter] readonly int? OctoVersionMajor;
    [Parameter] readonly int? OctoVersionMinor;
    [Parameter] readonly int? OctoVersionPatch;

    [Required]
    [OctoVersion(
        AutoDetectBranchParameter = nameof(OctoVersionAutoDetectBranch),
        BranchParameter = nameof(OctoVersionBranch),
        FullSemVerParameter = nameof(OctoVersionFullSemVer),
        MajorParameter = nameof(OctoVersionMajor),
        MinorParameter = nameof(OctoVersionMinor),
        PatchParameter = nameof(OctoVersionPatch))]
    readonly OctoVersionInfo OctoVersionInfo;

    static AbsolutePath SourceDirectory => RootDirectory / "source";
    static AbsolutePath ArtifactsDirectory => RootDirectory / "artifacts";
    static AbsolutePath PublishDirectory => RootDirectory / "publish";
    static AbsolutePath LocalPackagesDir => RootDirectory / ".." / "LocalPackages";

    Target Clean => _ => _
        .Before(Restore)
        .Executes(() =>
        {
            SourceDirectory.GlobDirectories("**/bin", "**/obj", "**/TestResults").ForEach(DeleteDirectory);
            EnsureCleanDirectory(ArtifactsDirectory);
            EnsureCleanDirectory(PublishDirectory);
        });

    Target Restore => _ => _
        .DependsOn(Clean)
        .Executes(() =>
        {
            DotNetRestore(_ => _
                .SetProjectFile(Solution));
        });

    Target Compile => _ => _
        .DependsOn(Clean)
        .DependsOn(Restore)
        .Executes(() =>
        {
            Logger.Info("Building LDAP Authentication Provider v{0}", OctoVersionInfo.FullSemVer);

            DotNetBuild(_ => _
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetVersion(OctoVersionInfo.FullSemVer)
                .EnableNoRestore());
        });

    Target Test => _ => _
        .DependsOn(Compile)
        .Executes(() =>
        {
            DotNetTest(_ => _
                .SetProjectFile(Solution)
                .SetConfiguration(Configuration)
                .SetNoBuild(true)
                .EnableNoRestore()
                .SetFilter(@"FullyQualifiedName\!~Integration.Tests"));
        });

    Target Pack => _ => _
        .DependsOn(Test)
        .Produces(ArtifactsDirectory / "*.nupkg")
        .Executes(() =>
        {
            Logger.Info("Packing LDAP Authentication Provider v{0}", OctoVersionInfo.FullSemVer);

            DotNetPack(_ => _
                .SetProject(Solution)
                .SetVersion(OctoVersionInfo.FullSemVer)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(ArtifactsDirectory)
                .EnableNoBuild()
                .DisableIncludeSymbols()
                .SetVerbosity(DotNetVerbosity.Normal)
                .SetProperty("NuspecFile", "../../build/Octopus.Server.Extensibility.Authentication.Ldap.nuspec")
                .SetProperty("NuspecProperties", $"Version={OctoVersionInfo.FullSemVer}"));

            DotNetPack(_ => _
                .SetProject(RootDirectory / "source/Client/Client.csproj")
                .SetVersion(OctoVersionInfo.FullSemVer)
                .SetConfiguration(Configuration)
                .SetOutputDirectory(ArtifactsDirectory)
                .EnableNoBuild()
                .DisableIncludeSymbols()
                .SetVerbosity(DotNetVerbosity.Normal));
        });

    Target CopyToLocalPackages => _ => _
        .OnlyWhenStatic(() => IsLocalBuild)
        .TriggeredBy(Pack)
        .Executes(() =>
        {
            EnsureExistingDirectory(LocalPackagesDir);
            ArtifactsDirectory.GlobFiles("*.nupkg")
                .ForEach(package =>
                {
                    CopyFileToDirectory(package, LocalPackagesDir);
                });
        });

    Target Default => _ => _
        .DependsOn(Pack);

    /// Support plugins are available for:
    /// - JetBrains ReSharper        https://nuke.build/resharper
    /// - JetBrains Rider            https://nuke.build/rider
    /// - Microsoft VisualStudio     https://nuke.build/visualstudio
    /// - Microsoft VSCode           https://nuke.build/vscode
    public static int Main() => Execute<Build>(x => x.Default);
}