/*
    ReleaseGen
    Copyright (C) 2024-2026  Alastair Lundy

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

namespace ReleaseGen.Cli;

/// <summary>
/// Shared command-line options for the ReleaseGen CLI.
/// </summary>
public static class CliOptions
{
    /// <summary>
    /// The only verbosity switch in v1: suppresses stage notes and progress while
    /// warnings and errors stay visible.
    /// </summary>
    public static readonly Option<bool> Quiet = new("--quiet")
    {
        Description = "Suppress stage notes and progress. Warnings and errors stay visible.",
        Recursive = true,
    };

    /// <summary>
    /// The dry-run switch every stage command and the chained run registers. A System.CommandLine
    /// symbol belongs to the command that adds it, so each command asks for its own instance;
    /// the spelling and description stay shared here so the flag cannot drift between stages.
    /// </summary>
    /// <returns>A fresh <c>--dry-run</c> option for one stage command.</returns>
    public static Option<bool> CreateDryRun() =>
        new("--dry-run")
        {
            Description =
                "Print the stage's would-be actions (spawned command lines and artifact paths) and write nothing anywhere, including release/ and TestResults/. The exit code reflects manifest validation errors only; a zero-exit dry run is a plan, not a certification.",
        };
}