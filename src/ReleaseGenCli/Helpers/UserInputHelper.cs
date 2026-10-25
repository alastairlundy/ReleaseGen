/*
    ReleaseGen
    Copyright (C) 2024-2026  Alastair Lundy

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

using ReleaseGen.Cli.Localizations;

namespace ReleaseGen.Cli.Helpers;

public static class UserInputHelper
{
    public static string GetRuntimeIdentifier()
    {
        string[] choices =
        [
            "win-x64",
            "win-arm64",
            "linux-x64",
            "linux-arm",
            "linux-arm64",
            "osx-x64",
            "osx-arm64",
        ];
        
        string output = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(
                    Resources.Prompts_Selection_StorageDrive.Replace(
                        Resources.Prompts_Highlights_StorageDrive,
                        $"[underline italic gold1]{Resources.Prompts_Highlights_StorageDrive}"
                        , StringComparison.Ordinal)
                )
                .PageSize(10)
                .AddChoices(choices)
                .MoreChoicesText($"({Resources.Prompts_Selection_RevealMoreOptions})")
        );

        return output;
    }
}