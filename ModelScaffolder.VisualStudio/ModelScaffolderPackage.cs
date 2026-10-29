using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace ModelScaffolder.VisualStudio
{
    /// <summary>
    ///     ModelScaffolder for Visual Studio: right-click a <c>*.model.json</c> in Solution Explorer and choose
    ///     "Scaffold Solution...". The generator itself runs out of process (bundled ModelScaffolder.Cli).
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("ModelScaffolder", "Scaffolds a layered .NET solution from a JSON model.", "1.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideUIContextRule(ModelFileSelectedContext,
        name: "ModelScaffolder: model file selected",
        expression: "ModelFile",
        termNames: new[] { "ModelFile" },
        termValues: new[] { @"HierSingleSelectionName:\.model\.json$" })]
    [Guid(PackageGuidString)]
    public sealed class ModelScaffolderPackage : AsyncPackage
    {
        public const string PackageGuidString = "e0d0c8b6-39ad-44e3-ac49-42daeda86b11";

        /// <summary>Active when exactly one file named *.model.json is selected; drives the command's visibility (see the .vsct).</summary>
        public const string ModelFileSelectedContext = "cfb245fe-2de2-447b-8cc7-a0fa755d8cf8";

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            await ScaffoldSolutionCommand.InitializeAsync(this);
        }
    }
}
