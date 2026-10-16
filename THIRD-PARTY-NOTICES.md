# Third-party notices

The project's own source code is covered by [LICENSE](LICENSE). That license does not replace the licenses of dependencies or of the .NET runtime included in self-contained builds. Preserve this file and the license/notice files accompanying redistributed components.

## Direct NuGet dependencies

| Component | Version | License / upstream terms |
| --- | --- | --- |
| Microsoft.Extensions.Configuration.Json | 10.0.0 | [MIT](https://github.com/dotnet/runtime/blob/v10.0.0/LICENSE.TXT) |
| Microsoft.Extensions.Http | 10.0.0 | [MIT](https://github.com/dotnet/runtime/blob/v10.0.0/LICENSE.TXT) |
| Microsoft.Extensions.Hosting | 10.0.0 | [MIT](https://github.com/dotnet/runtime/blob/v10.0.0/LICENSE.TXT) |
| Microsoft.Identity.Client | 4.77.1 | [MIT](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/main/LICENSE) |
| CsvHelper | 33.0.1 | [MS-PL OR Apache-2.0](https://www.nuget.org/packages/CsvHelper/33.0.1) |
| ScottPlot | 5.0.40 | [MIT](https://github.com/ScottPlot/ScottPlot/blob/main/LICENSE) |
| QuestPDF | 2024.7.1 | [Version-specific Community MIT / commercial terms](https://github.com/QuestPDF/QuestPDF/blob/2024.7.1/LICENSE.md) |

This table lists direct references in the project files. Restored packages also bring transitive components, including native rendering libraries. Consult the packages' own notices and licenses for the complete dependency graph of a particular build. Self-contained binaries also include Microsoft .NET runtime components under their accompanying notices.

## QuestPDF eligibility

The report utility selects `QuestPDF.Settings.License = LicenseType.Community`. The upstream license shipped with **2024.7.1** identifies open-source projects as eligible for free Community MIT use and permits redistribution of the compiled library with an application. This project is distributed as MIT source code.

Other uses, including modified proprietary applications, must be checked against the applicable QuestPDF terms. Do not infer unconditional eligibility from this repository's MIT license or from the runtime setting. QuestPDF's [current license information](https://www.questpdf.com/license) may differ from the license shipped with this pinned version. Reassess its terms when upgrading the package.

## Copyright attribution

- Microsoft packages: Copyright (c) Microsoft Corporation. All rights reserved.
- .NET components: Copyright (c) .NET Foundation and Contributors.
- CsvHelper: Copyright (c) 2009-2024 Josh Close.
- ScottPlot: Copyright (c) 2018 Scott Harden / Harden Technologies, LLC.
- QuestPDF: Copyright and ownership remain with QuestPDF and its respective contributors; see the version-specific upstream license and package notices.

## MIT license text

The following text applies to the MIT-licensed components listed above, with their respective copyright notices. QuestPDF eligibility is governed by its version-specific license selection terms.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

## CsvHelper license choice

CsvHelper is dual-licensed; this distribution uses the Microsoft Public License option. The project does not modify CsvHelper source code.

### Microsoft Public License (MS-PL)

This license governs use of the accompanying software. If you use the software, you accept this license. If you do not accept the license, do not use the software.

1. Definitions

   The terms "reproduce," "reproduction," "derivative works," and "distribution" have the same meaning here as under U.S. copyright law. A "contribution" is the original software, or any additions or changes to the software. A "contributor" is any person that distributes its contribution under this license. "Licensed patents" are a contributor's patent claims that read directly on its contribution.

2. Grant of Rights

   (A) Copyright Grant- Subject to the terms of this license, including the license conditions and limitations in section 3, each contributor grants you a non-exclusive, worldwide, royalty-free copyright license to reproduce its contribution, prepare derivative works of its contribution, and distribute its contribution or any derivative works that you create.

   (B) Patent Grant- Subject to the terms of this license, including the license conditions and limitations in section 3, each contributor grants you a non-exclusive, worldwide, royalty-free license under its licensed patents to make, have made, use, sell, offer for sale, import, and/or otherwise dispose of its contribution in the software or derivative works of the contribution in the software.

3. Conditions and Limitations

   (A) No Trademark License- This license does not grant you rights to use any contributors' name, logo, or trademarks.

   (B) If you bring a patent claim against any contributor over patents that you claim are infringed by the software, your patent license from such contributor to the software ends automatically.

   (C) If you distribute any portion of the software, you must retain all copyright, patent, trademark, and attribution notices that are present in the software.

   (D) If you distribute any portion of the software in source code form, you may do so only under this license by including a complete copy of this license with your distribution. If you distribute any portion of the software in compiled or object code form, you may only do so under a license that complies with this license.

   (E) The software is licensed "as-is." You bear the risk of using it. The contributors give no express warranties, guarantees, or conditions. You may have additional consumer rights under your local laws which this license cannot change. To the extent permitted under your local laws, the contributors exclude the implied warranties of merchantability, fitness for a particular purpose and non-infringement.
