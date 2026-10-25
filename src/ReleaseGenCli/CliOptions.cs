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
}