/*
    ReleaseGen
    Copyright (C) 2024-2026  Alastair Lundy

    This Source Code Form is subject to the terms of the Mozilla Public
    License, v. 2.0. If a copy of the MPL was not distributed with this
    file, You can obtain one at http://mozilla.org/MPL/2.0/.
*/

namespace ReleaseGen.Cli.Exceptions;

/// <summary>
/// Thrown when a pack invocation exits nonzero; the message names the target, the kind, and
/// the exit code, and the package stage command renders it as a one-line error.
/// </summary>
public sealed class PackageStageException(string message) : Exception(message);