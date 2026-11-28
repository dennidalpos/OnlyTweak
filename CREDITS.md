# Third-party credits: OnlyTweak

Reviewed on 2026-11-14 against the self-contained Windows x64 build, its embedded build evidence, resolved runtime assets, NuGet package metadata and the license texts supplied with those packages. OnlyTweak's own code remains proprietary under [LICENSE](LICENSE). Third-party terms apply separately; this inventory does not grant rights to OnlyTweak's code or certify legal compatibility.

## Distributed components

Versions below describe the reviewed build. Runtime-pack rows group their managed assemblies, native libraries and resources; they are not counts of individual libraries. Build tools, test dependencies and downloaded but unused runtime packs are excluded.

| Component | Version | License / SPDX identifier | Verified status | Notice or obligation |
| --- | --- | --- | --- | --- |
| [System.ServiceProcess.ServiceController](https://www.nuget.org/packages/System.ServiceProcess.ServiceController/10.0.14) | 10.0.14 | MIT | Resolved production dependency; package signature and declared MIT license checked; license text checked at the package's source commit | Retain copyright and MIT permission/disclaimer text: [license](licenses/dotnet/LICENSE-SERVICECONTROLLER.txt). Package copyright: © Microsoft Corporation. All rights reserved. |
| [Microsoft.NETCore.App.Runtime.win-x64](https://www.nuget.org/packages/Microsoft.NETCore.App.Runtime.win-x64/10.0.15) | 10.0.15 | MIT in package metadata; `LicenseRef-Microsoft-DotNet-Library` for the Windows binaries identified below; additional upstream notices | Runtime pack recorded in the actual bundle; signed package and included license/notice files checked | Keep the [MIT text](licenses/dotnet/LICENSE-MIT.txt), [.NET Library terms](licenses/dotnet/LICENSE-DOTNET-LIBRARY.txt) and [upstream notices](licenses/dotnet/THIRD-PARTY-NOTICES.txt). Do not classify the entire Windows runtime as MIT-only. |
| [Microsoft.WindowsDesktop.App.Runtime.win-x64](https://www.nuget.org/packages/Microsoft.WindowsDesktop.App.Runtime.win-x64/10.0.15) | 10.0.15 | MIT for the resolved Windows Forms assets | Runtime pack recorded in the actual bundle; signed package, supplied license and selected assets checked | Retain the [supplied license](licenses/dotnet/LICENSE-WINDOWSDESKTOP.txt). The resolved assets do not include WPF's native binaries or D3DCompiler_47_cor3.dll. |
| [Microsoft.NETCore.App.Host.win-x64](https://www.nuget.org/packages/Microsoft.NETCore.App.Host.win-x64/10.0.15) | 10.0.15 | MIT in package metadata; Windows single-file runtime terms as below | Apphost input resolved from the installed SDK pack; version and license checked against the matching NuGet metadata and source commit | Retain the [source MIT license](licenses/dotnet/LICENSE-APPHOST.txt); preserve the Windows runtime terms when distributing the self-contained executable. |
| [MsQuic](https://github.com/microsoft/msquic/tree/v2.5.12) — included within the .NET runtime row | 2.5.12.161031966-official | MIT | `msquic.dll` is a resolved native runtime asset; file version and the runtime's MsQuic notice checked against the versioned upstream license | Retain Microsoft's copyright and the [MsQuic MIT license](licenses/dotnet/LICENSE-MSQUIC.txt). This row is a nested component, not an additional runtime pack. |

## Windows runtime terms and review points

[Microsoft's Windows licensing guidance](https://github.com/dotnet/core/blob/main/license-information-windows.md) identifies `coreclr.dll`, runtimes included in single-file executables, and `Microsoft.DiaSymReader.Native.*.dll` under the [.NET Library License](https://dotnet.microsoft.com/en-us/dotnet_library_license.htm). The reviewed build contains `coreclr.dll` and `Microsoft.DiaSymReader.Native.amd64.dll`. The MIT declaration in the runtime-pack metadata alone does not describe all these Windows-specific terms.

`LicenseRef-Microsoft-DotNet-Library` is a local descriptive identifier for those terms, not an assertion that Microsoft publishes an SPDX-listed license with that identifier. The supplied [text](licenses/dotnet/LICENSE-DOTNET-LIBRARY.txt) is copied unchanged from the source commit declared by the runtime pack. Microsoft's current linked license page has different wording; the governing version and any required end-user/distributor terms need owner/legal review before redistribution. This audit does not replace or amend OnlyTweak's LICENSE.

The .NET Library terms impose distribution conditions beyond attribution, including protective end-user/distributor terms and restrictions on standalone redistribution. Copying notices does not by itself establish compliance. No conclusion of unrestricted redistribution, absence of conflicts or legal clearance is made.

The runtime's [third-party notice file](licenses/dotnet/THIRD-PARTY-NOTICES.txt) is preserved verbatim. It is an upstream omnibus notice covering multiple platforms and uses; its entries are not a claim that every named project is a separate dependency shipped by OnlyTweak. Exact versions and Windows-binary mapping for all embedded source fragments are not independently established. Names, copyright statements and license texts remain available there; an exhaustive per-fragment SPDX/SBOM and copyleft clearance remain unverified.

## Fonts, icons and external Windows tools

The UI resolves Segoe UI, Segoe Fluent Icons or Segoe MDL2 Assets from Windows. No font files or third-party icon library are embedded or copied. These system fonts are not being relicensed or redistributed; see [Microsoft's font redistribution guidance](https://learn.microsoft.com/en-us/typography/fonts/font-faq).

The OnlyTweak application icon is built from the repository's SVG by `scripts/create-icon.ps1`; the SVG, PNG and ICO are project assets, not an imported icon pack. No external image/font payload was found in the embedded resources. Independent proof of original asset authorship is outside this technical inventory.

Windows PowerShell, Windows command-line tools and the Windows settings UI are invoked from the host installation; they are not bundled. Their presence and rights are governed separately by the Windows installation.

## Distribution and maintenance

Keep [NOTICE.md](NOTICE.md), this file, [LICENSE](LICENSE) and the complete `licenses/` directory with the executable. Publishing copies them as companion files; the local delivery script copies them and verifies their hashes. The program remains a self-contained executable, but the executable alone is not the complete redistribution package.

Update this inventory and its source texts whenever resolved dependencies, runtime packs, apphost or embedded assets change. Do not infer shipped components from cache contents, the test project or all SDK download dependencies. The build receipt binds these notice files to the reviewed executable; rebuild after changing them.
