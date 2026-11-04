# MSI installer notices

The MSI uses the unmodified WiX Toolset UI Extension 4.0.6, including its
`WixUIPrintEula_X64` helper. Copyright (c) .NET Foundation and contributors.
This component is licensed under the Microsoft Reciprocal License (MS-RL).

The license is included as `Licenses/WiX-LICENSE.txt`. The corresponding WiX
source, including the UI helper and its supporting libraries, is included as
`Licenses/WiX-4.0.6-source.zip` from the upstream repository at commit
`73c897383236ddbbbc6ba257634013c1269ceec2`:

https://github.com/wixtoolset/wix/tree/73c897383236ddbbbc6ba257634013c1269ceec2

The source archive SHA-256 is
`ba8c6799bcfcaa3b475f8a94251f35b602eb475ad563ed5ee205ed5be84443ce`.
WiX itself is a build tool and is not installed as an application dependency.
These MSI-only notices supplement `THIRD-PARTY-NOTICES.md`; ArkTerminal's
application license and runtime dependencies remain unchanged.
